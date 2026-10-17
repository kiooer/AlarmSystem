using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Application.Abstractions.Sources;
using AlarmSystem.Application.Polling;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Application.Tests.Polling;

public sealed class AlarmPollingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime SourceNow = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task PollOnce_IngestsAlarmAndOperatorWithoutTreatingAckTextAsAck()
    {
        var alarm = Event(Guid.NewGuid(), SourceNow.AddMinutes(-2), "ALARM") with
        {
            RawPriority = "UNKNOWN-P1",
            RawUnit = "bar",
        };
        var operatorEvent = Event(Guid.NewGuid(), SourceNow.AddMinutes(-1), "OPERATOR") with
        {
            TagName = null,
            Description = "ACK PUMP-01",
        };
        var (service, repository, _, _) = CreateService([alarm], [operatorEvent]);

        var result = await service.PollOnceAsync();

        Assert.Equal(2, result.InsertedCount);
        var savedAlarm = Assert.Single(
            repository.Events,
            item => item.EventType == AlarmEventType.Alarm);
        Assert.Null(savedAlarm.AlarmLevel);
        Assert.Equal("UNKNOWN-P1", savedAlarm.RawPriority);
        Assert.Equal("bar", savedAlarm.RawUnit);
        Assert.Contains("PriorityMappingPending", savedAlarm.DataQualityFlags);

        var savedOperator = Assert.Single(
            repository.Events,
            item => item.EventType == AlarmEventType.Operator);
        Assert.Equal("OPERATOR", savedOperator.OriginalMessageType);
        Assert.Equal("NotApplicable", savedOperator.AckMatchStatus);
        Assert.Null(savedOperator.TagName);
        Assert.Equal("ACK PUMP-01", savedOperator.Description);
        Assert.DoesNotContain(repository.Events, item => item.EventType == AlarmEventType.Ack);
    }

    [Fact]
    public async Task PollOnce_RereadCreatesDuplicatesWithoutNewRows()
    {
        var alarm = Event(Guid.NewGuid(), SourceNow.AddSeconds(-30), "ALARM");
        var (service, repository, _, _) = CreateService([alarm], []);

        var first = await service.PollOnceAsync();
        var second = await service.PollOnceAsync();

        Assert.Equal(1, first.InsertedCount);
        Assert.Equal(0, second.InsertedCount);
        Assert.Equal(1, second.DuplicateCount);
        Assert.Single(repository.Events);
    }

    [Fact]
    public async Task PollOnce_PreservesDifferentGuidsAtTheSameTimestamp()
    {
        var timestamp = SourceNow.AddMinutes(-3);
        var events = new[]
        {
            Event(Guid.NewGuid(), timestamp, "ALARM") with { RawValue = "1" },
            Event(Guid.NewGuid(), timestamp, "ALARM") with { RawValue = "2" },
        };
        var (service, repository, _, _) = CreateService(events, []);

        await service.PollOnceAsync();

        Assert.Equal(2, repository.Events.Count);
        Assert.Equal(new[] { "1", "2" }, repository.Events.Select(item => item.RawValue).Order().ToArray());
    }

    [Fact]
    public async Task PollOnce_PagesOnlyAcrossCompleteTimestampGroups()
    {
        var events = new[]
        {
            Event(Guid.NewGuid(), SourceNow.AddMinutes(-3), "ALARM"),
            Event(Guid.NewGuid(), SourceNow.AddMinutes(-2), "ALARM"),
            Event(Guid.NewGuid(), SourceNow.AddMinutes(-2), "ALARM"),
            Event(Guid.NewGuid(), SourceNow.AddMinutes(-1), "ALARM"),
        };
        var options = ValidOptions(maxBatchSize: 2);
        var (service, repository, _, _) = CreateService(events, [], options);

        var result = await service.PollOnceAsync();

        Assert.Equal(4, repository.Events.Count);
        Assert.True(result.Streams[0].PageCount >= 3);
    }

    [Fact]
    public async Task PollOnce_RefusesTimestampGroupLargerThanBatch()
    {
        var timestamp = SourceNow.AddMinutes(-1);
        var events = new[]
        {
            Event(Guid.NewGuid(), timestamp, "ALARM"),
            Event(Guid.NewGuid(), timestamp, "ALARM"),
            Event(Guid.NewGuid(), timestamp, "ALARM"),
        };
        var (service, repository, _, _) = CreateService(events, [], ValidOptions(maxBatchSize: 2));

        var exception = await Assert.ThrowsAsync<AlarmPollingException>(() => service.PollOnceAsync());

        Assert.Equal("timestamp_group_too_large", exception.ErrorCode);
        Assert.Empty(repository.Events);
        Assert.Null(repository.Checkpoints[(AlarmPollingService.SourceSystem, AlarmPollingStreams.Alarm)].WatermarkSourceTime);
    }

    [Fact]
    public async Task PollOnce_NullGuidIsUnresolvableAndDoesNotAdvanceCheckpoint()
    {
        var (service, repository, _, _) = CreateService(
            [Event(null, SourceNow.AddMinutes(-1), "ALARM")],
            []);

        var exception = await Assert.ThrowsAsync<AlarmPollingException>(() => service.PollOnceAsync());

        Assert.Equal("unresolvable_source_identity", exception.ErrorCode);
        Assert.Empty(repository.Events);
        var checkpoint = repository.Checkpoints[(AlarmPollingService.SourceSystem, AlarmPollingStreams.Alarm)];
        Assert.Null(checkpoint.WatermarkSourceTime);
        Assert.Equal("unresolvable_source_identity", checkpoint.LastErrorCode);
    }

    [Fact]
    public async Task PollOnce_MissingSourceTimeDoesNotInventTimestampOrAdvanceCheckpoint()
    {
        var (service, repository, _, _) = CreateService(
            [Event(Guid.NewGuid(), null, "ALARM")],
            []);

        var exception = await Assert.ThrowsAsync<AlarmPollingException>(() => service.PollOnceAsync());

        Assert.Equal("source_timestamp_missing", exception.ErrorCode);
        Assert.Empty(repository.Events);
        Assert.Null(repository.Checkpoints[(AlarmPollingService.SourceSystem, AlarmPollingStreams.Alarm)].WatermarkSourceTime);
    }

    [Fact]
    public async Task PollOnce_DuplicateGuidWithDifferentContentRollsBackAndRecordsCollision()
    {
        var id = Guid.NewGuid();
        var alarmReader = new FakeAlarmSourceReader(
            AlarmPollingStreams.Alarm,
            [Event(id, SourceNow.AddMinutes(-1), "ALARM") with { RawValue = "1" }]);
        var operatorReader = new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, []);
        var repository = new FakeIngestionRepository();
        var service = CreateService(alarmReader, operatorReader, repository);
        await service.PollOnceAsync();
        var watermark = repository.Checkpoints[(AlarmPollingService.SourceSystem, AlarmPollingStreams.Alarm)]
            .WatermarkSourceTime;
        alarmReader.Events[0] = alarmReader.Events[0] with { RawValue = "2" };

        var exception = await Assert.ThrowsAsync<SourceIdentityCollisionException>(() =>
            service.PollOnceAsync());

        Assert.Equal("identity_collision", exception.ErrorCode);
        Assert.Single(repository.Events);
        Assert.Equal("1", repository.Events[0].RawValue);
        Assert.Equal(
            watermark,
            repository.Checkpoints[(AlarmPollingService.SourceSystem, AlarmPollingStreams.Alarm)]
                .WatermarkSourceTime);
    }

    [Fact]
    public async Task PollOnce_OverlapCapturesDelayedSourceRow()
    {
        var original = Event(Guid.NewGuid(), SourceNow.AddSeconds(-30), "ALARM");
        var alarmReader = new FakeAlarmSourceReader(AlarmPollingStreams.Alarm, [original]);
        var operatorReader = new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, []);
        var repository = new FakeIngestionRepository();
        var service = CreateService(alarmReader, operatorReader, repository);
        await service.PollOnceAsync();
        alarmReader.Events.Add(Event(Guid.NewGuid(), SourceNow.AddSeconds(-20), "ALARM"));

        var result = await service.PollOnceAsync();

        Assert.Equal(1, result.Streams[0].InsertedCount);
        Assert.Equal(1, result.Streams[0].DuplicateCount);
        Assert.Equal(2, repository.Events.Count);
    }

    [Fact]
    public async Task PollOnce_CommitFailureCanBeReplayedAfterRestart()
    {
        var alarm = Event(Guid.NewGuid(), SourceNow.AddMinutes(-1), "ALARM");
        var alarmReader = new FakeAlarmSourceReader(AlarmPollingStreams.Alarm, [alarm]);
        var operatorReader = new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, []);
        var repository = new FakeIngestionRepository { FailNextCommit = true };
        var service = CreateService(alarmReader, operatorReader, repository);

        var exception = await Assert.ThrowsAsync<AlarmPollingException>(() => service.PollOnceAsync());
        Assert.Equal("simulated_commit_failure", exception.ErrorCode);
        Assert.Empty(repository.Events);

        var restartedService = CreateService(alarmReader, operatorReader, repository);
        var result = await restartedService.PollOnceAsync();

        Assert.Equal(1, result.InsertedCount);
        Assert.Single(repository.Events);
    }

    [Fact]
    public async Task PollOnce_CallerCancellationPropagatesWithoutCheckpointMutation()
    {
        var alarmReader = new FakeAlarmSourceReader(AlarmPollingStreams.Alarm, [])
        {
            CancelOnRead = true,
        };
        var repository = new FakeIngestionRepository();
        var service = CreateService(
            alarmReader,
            new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, []),
            repository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PollOnceAsync(cancellation.Token));

        Assert.Empty(repository.Events);
        Assert.Empty(repository.Checkpoints);
    }

    [Fact]
    public async Task PollOnce_SourceTimeoutDoesNotAdvanceCheckpoint()
    {
        var alarmReader = new FakeAlarmSourceReader(AlarmPollingStreams.Alarm, [])
        {
            Failure = new AlarmPollingException(
                "source_sql_timeout",
                "Simulated source query timeout."),
        };
        var repository = new FakeIngestionRepository();
        var service = CreateService(
            alarmReader,
            new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, []),
            repository);

        var exception = await Assert.ThrowsAsync<AlarmPollingException>(() =>
            service.PollOnceAsync());

        Assert.Equal("source_sql_timeout", exception.ErrorCode);
        Assert.Empty(repository.Events);
        Assert.Null(repository.Checkpoints[(AlarmPollingService.SourceSystem, AlarmPollingStreams.Alarm)]
            .WatermarkSourceTime);
    }

    [Fact]
    public async Task PollOnce_AmbiguousLocalTimeIsStoredWithoutUnsupportedUtcGuess()
    {
        var sourceTime = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);
        var options = ValidOptions(timeZoneId: "Pacific Standard Time");
        var utcNow = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var alarmReader = new FakeAlarmSourceReader(
            AlarmPollingStreams.Alarm,
            [Event(Guid.NewGuid(), sourceTime, "ALARM")]);
        var repository = new FakeIngestionRepository();
        var service = new AlarmPollingService(
            [alarmReader, new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, [])],
            repository,
            options,
            new FixedTimeProvider(utcNow));

        await service.PollOnceAsync();

        var saved = Assert.Single(repository.Events);
        Assert.Null(saved.EventTimeUtc);
        Assert.Contains("SourceTimeAmbiguous", saved.DataQualityFlags);
    }

    private static (AlarmPollingService Service, FakeIngestionRepository Repository,
        FakeAlarmSourceReader AlarmReader, FakeAlarmSourceReader OperatorReader) CreateService(
        IEnumerable<SourceAlarmEvent> alarmEvents,
        IEnumerable<SourceAlarmEvent> operatorEvents,
        AlarmPollingOptions? options = null)
    {
        var alarmReader = new FakeAlarmSourceReader(AlarmPollingStreams.Alarm, alarmEvents);
        var operatorReader = new FakeAlarmSourceReader(AlarmPollingStreams.AckOperator, operatorEvents);
        var repository = new FakeIngestionRepository();
        return (
            CreateService(alarmReader, operatorReader, repository, options),
            repository,
            alarmReader,
            operatorReader);
    }

    private static AlarmPollingService CreateService(
        FakeAlarmSourceReader alarmReader,
        FakeAlarmSourceReader operatorReader,
        FakeIngestionRepository repository,
        AlarmPollingOptions? options = null) =>
        new(
            [alarmReader, operatorReader],
            repository,
            options ?? ValidOptions(),
            new FixedTimeProvider(Now));

    private static AlarmPollingOptions ValidOptions(
        int maxBatchSize = 500,
        string timeZoneId = "Taipei Standard Time",
        int initialLookbackHours = 24) => new()
        {
            Enabled = true,
            IntervalSeconds = 5,
            OverlapSeconds = 60,
            MaxBatchSize = maxBatchSize,
            CommandTimeoutSeconds = 10,
            InitialLookbackHours = initialLookbackHours,
            MaxFailureBackoffSeconds = 60,
            SourceTimeZoneId = timeZoneId,
            SourceConfigPath = "source.json",
            AlarmStoreConfigPath = "store.json",
        };

    private static SourceAlarmEvent Event(Guid? id, DateTime? time, string messageType) => new(
        id,
        time,
        "NODE-1",
        "OP-1",
        "TAG-1",
        messageType,
        "ACTIVE",
        "P1",
        "AREA-1",
        "10",
        "測試警報",
        "unit");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAlarmSourceReader(
        string streamName,
        IEnumerable<SourceAlarmEvent> events) : IAlarmSourceReader
    {
        public string StreamName { get; } = streamName;

        public List<SourceAlarmEvent> Events { get; } = [.. events];

        public bool CancelOnRead { get; init; }

        public AlarmPollingException? Failure { get; init; }

        public Task<AlarmSourceReadResult> ReadAsync(
            AlarmSourceReadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (CancelOnRead)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var missingTimestamp = Events.FirstOrDefault(item => item.SourceEventTime is null);
            if (missingTimestamp is not null)
            {
                return Task.FromResult(new AlarmSourceReadResult([missingTimestamp], false));
            }

            var candidates = Events
                .Where(item => item.SourceEventTime >= request.LowerBoundInclusive)
                .Where(item => item.SourceEventTime < request.UpperBoundExclusive)
                .Where(item => request.AfterTimestampExclusive is null ||
                    item.SourceEventTime > request.AfterTimestampExclusive)
                .OrderBy(item => item.SourceEventTime)
                .ThenBy(item => item.SourceMessageId)
                .Take(request.MaxBatchSize + 1)
                .ToArray();
            if (candidates.Length <= request.MaxBatchSize)
            {
                return Task.FromResult(new AlarmSourceReadResult(candidates, false));
            }

            var boundary = candidates[request.MaxBatchSize].SourceEventTime!.Value;
            var complete = candidates.TakeWhile(item => item.SourceEventTime < boundary).ToArray();
            return Task.FromResult(complete.Length == 0
                ? new AlarmSourceReadResult([], true, boundary)
                : new AlarmSourceReadResult(complete, true));
        }
    }

    private sealed class FakeIngestionRepository : IAlarmEventIngestionRepository
    {
        public List<AlarmEventIngestionItem> Events { get; } = [];

        public Dictionary<(string SourceSystem, string Stream), PollingCheckpoint> Checkpoints { get; } = [];

        public bool FailNextCommit { get; set; }

        public Task<PollingCheckpoint?> GetCheckpointAsync(
            string sourceSystem,
            string streamName,
            CancellationToken cancellationToken = default)
        {
            Checkpoints.TryGetValue((sourceSystem, streamName), out var checkpoint);
            return Task.FromResult(checkpoint);
        }

        public Task<AlarmEventIngestionResult> CommitBatchAsync(
            string sourceSystem,
            string streamName,
            IReadOnlyList<AlarmEventIngestionItem> events,
            DateTime watermarkSourceTime,
            Guid? lastProcessedMessageId,
            DateTime completedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new AlarmPollingException(
                    "simulated_commit_failure",
                    "The simulated transaction was interrupted before commit.");
            }

            var staged = Events.ToList();
            var inserted = 0;
            var duplicate = 0;
            foreach (var item in events)
            {
                var existing = staged.SingleOrDefault(candidate =>
                    candidate.SourceMessageId == item.SourceMessageId);
                if (existing is null)
                {
                    staged.Add(item);
                    inserted++;
                }
                else if (Canonical(existing) == Canonical(item))
                {
                    duplicate++;
                }
                else
                {
                    throw new SourceIdentityCollisionException(item.SourceMessageId);
                }
            }

            Events.Clear();
            Events.AddRange(staged);
            Checkpoints.TryGetValue((sourceSystem, streamName), out var current);
            var advances = current?.WatermarkSourceTime is null ||
                watermarkSourceTime > current.WatermarkSourceTime.Value;
            Checkpoints[(sourceSystem, streamName)] = new PollingCheckpoint(
                sourceSystem,
                streamName,
                advances ? watermarkSourceTime : current!.WatermarkSourceTime,
                advances ? lastProcessedMessageId : current!.LastProcessedMessageId,
                completedAtUtc,
                null);
            return Task.FromResult(new AlarmEventIngestionResult(inserted, duplicate));
        }

        public Task RecordFailureAsync(
            string sourceSystem,
            string streamName,
            string errorCode,
            DateTime failedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Checkpoints.TryGetValue((sourceSystem, streamName), out var current);
            Checkpoints[(sourceSystem, streamName)] = new PollingCheckpoint(
                sourceSystem,
                streamName,
                current?.WatermarkSourceTime,
                current?.LastProcessedMessageId,
                current?.LastCompletedAtUtc,
                errorCode);
            return Task.CompletedTask;
        }

        private static AlarmEventIngestionItem Canonical(AlarmEventIngestionItem item) =>
            item with { EventId = Guid.Empty, IngestedAtUtc = default };
    }
}
