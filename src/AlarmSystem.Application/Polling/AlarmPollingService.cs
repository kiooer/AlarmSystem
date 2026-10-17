using System.Diagnostics;
using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Application.Abstractions.Sources;

namespace AlarmSystem.Application.Polling;

public sealed class AlarmPollingService
{
    public const string SourceSystem = "iFIX";

    private readonly IReadOnlyDictionary<string, IAlarmSourceReader> _readers;
    private readonly IAlarmEventIngestionRepository _repository;
    private readonly AlarmPollingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _sourceTimeZone;

    public AlarmPollingService(
        IEnumerable<IAlarmSourceReader> readers,
        IAlarmEventIngestionRepository repository,
        AlarmPollingOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!options.Enabled)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPollingService cannot be created while AlarmPolling.Enabled is false.");
        }

        _readers = readers.ToDictionary(reader => reader.StreamName, StringComparer.Ordinal);
        foreach (var stream in new[] { AlarmPollingStreams.Alarm, AlarmPollingStreams.AckOperator })
        {
            if (!_readers.ContainsKey(stream))
            {
                throw new AlarmPollingConfigurationException(
                    $"Exactly one source reader is required for stream '{stream}'.");
            }
        }

        if (_readers.Count != 2)
        {
            throw new AlarmPollingConfigurationException(
                "Only the ALARM and ACK_OPERATOR readers may be registered for stage 2A.");
        }

        _repository = repository;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.SourceTimeZoneId);
    }

    public async Task<AlarmPollingResult> PollOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<AlarmStreamPollingResult>(2);
        foreach (var stream in new[] { AlarmPollingStreams.Alarm, AlarmPollingStreams.AckOperator })
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await PollStreamAsync(stream, cancellationToken));
        }

        return new AlarmPollingResult(results, stopwatch.Elapsed);
    }

    private async Task<AlarmStreamPollingResult> PollStreamAsync(
        string streamName,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var checkpoint = await _repository.GetCheckpointAsync(
                SourceSystem,
                streamName,
                cancellationToken);
            var upperBound = ToSourceLocalTime(_timeProvider.GetUtcNow());
            var usedInitialLookback = checkpoint?.WatermarkSourceTime is null;
            var lowerBound = usedInitialLookback
                ? upperBound.AddHours(-_options.InitialLookbackHours)
                : AsUnspecified(checkpoint!.WatermarkSourceTime!.Value)
                    .AddSeconds(-_options.OverlapSeconds);
            DateTime? cursor = null;
            var readCount = 0;
            var insertedCount = 0;
            var duplicateCount = 0;
            var pageCount = 0;
            var unmappedPriorityCount = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await _readers[streamName].ReadAsync(
                    new AlarmSourceReadRequest(
                        lowerBound,
                        upperBound,
                        cursor,
                        _options.MaxBatchSize),
                    cancellationToken);
                pageCount++;

                if (batch.OversizedTimestamp is DateTime oversizedTimestamp)
                {
                    throw new AlarmPollingException(
                        "timestamp_group_too_large",
                        $"Stream '{streamName}' contains more than {_options.MaxBatchSize} rows at source timestamp {oversizedTimestamp:O}; checkpoint advancement was refused.");
                }

                var prepared = PrepareBatch(streamName, batch.Events, lowerBound, upperBound, cursor);
                readCount += prepared.Count;
                unmappedPriorityCount += prepared.Count(item =>
                    item.DataQualityFlags?.Contains("Priority", StringComparison.Ordinal) == true);

                if (prepared.Count == 0)
                {
                    await _repository.CommitBatchAsync(
                        SourceSystem,
                        streamName,
                        [],
                        upperBound,
                        null,
                        UtcNow(),
                        cancellationToken);
                    break;
                }

                var lastSourceTime = prepared.Max(item => item.SourceEventTime);
                var isWindowComplete = !batch.HasMore;
                var committedWatermark = isWindowComplete ? upperBound : lastSourceTime;
                Guid? lastMessageId = isWindowComplete
                    ? null
                    : prepared
                        .Where(item => item.SourceEventTime == lastSourceTime)
                        .OrderBy(item => item.SourceMessageId)
                        .Last()
                        .SourceMessageId;
                var ingestion = await _repository.CommitBatchAsync(
                    SourceSystem,
                    streamName,
                    prepared,
                    committedWatermark,
                    lastMessageId,
                    UtcNow(),
                    cancellationToken);
                insertedCount += ingestion.InsertedCount;
                duplicateCount += ingestion.DuplicateCount;

                if (isWindowComplete)
                {
                    break;
                }

                if (cursor is not null && lastSourceTime <= cursor.Value)
                {
                    throw new AlarmPollingException(
                        "source_cursor_not_advancing",
                        $"Stream '{streamName}' did not advance beyond its prior complete timestamp group.");
                }

                cursor = lastSourceTime;
            }

            return new AlarmStreamPollingResult(
                streamName,
                usedInitialLookback,
                lowerBound,
                upperBound,
                readCount,
                insertedCount,
                duplicateCount,
                pageCount,
                unmappedPriorityCount,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AlarmPollingException exception)
        {
            await TryRecordFailureAsync(streamName, exception.ErrorCode, cancellationToken);
            throw;
        }
    }

    private List<AlarmEventIngestionItem> PrepareBatch(
        string streamName,
        IReadOnlyList<SourceAlarmEvent> sourceEvents,
        DateTime lowerBound,
        DateTime upperBound,
        DateTime? cursor)
    {
        var expectedMessageType = AlarmPollingStreams.GetMessageType(streamName);
        var prepared = new List<AlarmEventIngestionItem>(sourceEvents.Count);
        DateTime? previousTimestamp = null;
        foreach (var sourceEvent in sourceEvents)
        {
            if (!string.Equals(
                sourceEvent.OriginalMessageType,
                expectedMessageType,
                StringComparison.Ordinal))
            {
                throw new AlarmPollingException(
                    "source_message_type_mismatch",
                    $"Stream '{streamName}' returned a row with an unexpected message type.");
            }

            if (sourceEvent.SourceEventTime is not DateTime rawSourceTime)
            {
                throw new AlarmPollingException(
                    "source_timestamp_missing",
                    $"Stream '{streamName}' returned a row without ALM_NATIVETIMEIN; checkpoint advancement was refused.");
            }

            var sourceTime = AsUnspecified(rawSourceTime);
            if (sourceTime < lowerBound || sourceTime >= upperBound ||
                cursor is not null && sourceTime <= cursor.Value ||
                previousTimestamp is not null && sourceTime < previousTimestamp.Value)
            {
                throw new AlarmPollingException(
                    "source_reader_contract_violation",
                    $"Stream '{streamName}' returned a row outside the requested ordered time window.");
            }

            if (sourceEvent.SourceMessageId is not Guid sourceMessageId)
            {
                throw new AlarmPollingException(
                    "unresolvable_source_identity",
                    $"Stream '{streamName}' returned a row without ALM_MSGID; stage 2A cannot prove whether identical rows are distinct, so checkpoint advancement was refused.");
            }

            ValidateLengths(sourceEvent);
            var qualityFlags = new List<string>();
            if (string.IsNullOrWhiteSpace(sourceEvent.RawPriority))
            {
                qualityFlags.Add("PriorityMissing");
            }
            else
            {
                qualityFlags.Add("PriorityMappingPending");
            }

            DateTime? eventTimeUtc = null;
            if (_sourceTimeZone.IsInvalidTime(sourceTime))
            {
                qualityFlags.Add("SourceTimeInvalid");
            }
            else if (_sourceTimeZone.IsAmbiguousTime(sourceTime))
            {
                qualityFlags.Add("SourceTimeAmbiguous");
            }
            else
            {
                eventTimeUtc = TimeZoneInfo.ConvertTimeToUtc(sourceTime, _sourceTimeZone);
            }

            prepared.Add(new AlarmEventIngestionItem(
                Guid.NewGuid(),
                sourceMessageId,
                sourceEvent.OriginalMessageType,
                AlarmPollingStreams.GetEventType(streamName),
                sourceEvent.SourceNode,
                sourceEvent.OperatorNode,
                sourceEvent.TagName,
                sourceEvent.AlarmStatus,
                null,
                sourceEvent.Area,
                sourceEvent.RawValue,
                sourceEvent.Description,
                sourceEvent.RawPriority,
                sourceEvent.RawUnit,
                sourceTime,
                _options.SourceTimeZoneId,
                eventTimeUtc,
                UtcNow(),
                "NotApplicable",
                string.Join('|', qualityFlags)));
            previousTimestamp = sourceTime;
        }

        return prepared;
    }

    private static void ValidateLengths(SourceAlarmEvent sourceEvent)
    {
        ValidateLength(sourceEvent.OriginalMessageType, 32, nameof(sourceEvent.OriginalMessageType));
        ValidateLength(sourceEvent.SourceNode, 128, nameof(sourceEvent.SourceNode));
        ValidateLength(sourceEvent.OperatorNode, 128, nameof(sourceEvent.OperatorNode));
        ValidateLength(sourceEvent.TagName, 256, nameof(sourceEvent.TagName));
        ValidateLength(sourceEvent.AlarmStatus, 32, nameof(sourceEvent.AlarmStatus));
        ValidateLength(sourceEvent.Area, 256, nameof(sourceEvent.Area));
        ValidateLength(sourceEvent.RawValue, 256, nameof(sourceEvent.RawValue));
        ValidateLength(sourceEvent.Description, 2048, nameof(sourceEvent.Description));
        ValidateLength(sourceEvent.RawPriority, 128, nameof(sourceEvent.RawPriority));
        ValidateLength(sourceEvent.RawUnit, 128, nameof(sourceEvent.RawUnit));
    }

    private static void ValidateLength(string? value, int maximum, string field)
    {
        if (value?.Length > maximum)
        {
            throw new AlarmPollingException(
                "source_value_too_long",
                $"Source field '{field}' exceeds the supported length of {maximum}; checkpoint advancement was refused.");
        }
    }

    private async Task TryRecordFailureAsync(
        string streamName,
        string errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RecordFailureAsync(
                SourceSystem,
                streamName,
                errorCode,
                UtcNow(),
                cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            // Preserve the original classified polling failure. The Worker logs that exception;
            // a simultaneous AlarmStore failure may prevent LastErrorCode from being persisted.
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private DateTime ToSourceLocalTime(DateTimeOffset utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(utc, _sourceTimeZone).DateTime,
            DateTimeKind.Unspecified);

    private static DateTime AsUnspecified(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
}
