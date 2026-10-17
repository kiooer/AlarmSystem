using System.Data;
using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Application.Polling;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed class SqlAlarmEventIngestionRepository : IAlarmEventIngestionRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly int _commandTimeoutSeconds;

    public SqlAlarmEventIngestionRepository(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory,
        int commandTimeoutSeconds)
    {
        if (options.Role != SqlServerConnectionRole.AlarmStore)
        {
            throw new SqlConfigurationException(
                "Alarm event ingestion requires a SqlServer.Role of AlarmStore.");
        }

        if (commandTimeoutSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeoutSeconds),
                "The AlarmStore SQL command timeout must be between 1 and 120 seconds.");
        }

        _connectionFactory = connectionFactory;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public async Task<PollingCheckpoint?> GetCheckpointAsync(
        string sourceSystem,
        string streamName,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(sourceSystem, streamName);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SourceSystem, StreamName, WatermarkSourceTime,
                LastProcessedMessageId, LastCompletedAtUtc, LastErrorCode
            FROM alarm.PollingCheckpoints
            WHERE SourceSystem = @sourceSystem AND StreamName = @streamName;
            """;
        command.CommandTimeout = _commandTimeoutSeconds;
        AddKeyParameters(command, sourceSystem, streamName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PollingCheckpoint(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public async Task<AlarmEventIngestionResult> CommitBatchAsync(
        string sourceSystem,
        string streamName,
        IReadOnlyList<AlarmEventIngestionItem> events,
        DateTime watermarkSourceTime,
        Guid? lastProcessedMessageId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(sourceSystem, streamName);
        ArgumentNullException.ThrowIfNull(events);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var existingCheckpoint = await ReadCheckpointForUpdateAsync(
                connection,
                transaction,
                sourceSystem,
                streamName,
                cancellationToken);
            var inserted = 0;
            var duplicates = 0;
            foreach (var item in events.OrderBy(item => item.SourceMessageId))
            {
                var existing = await ReadExistingEventAsync(
                    connection,
                    transaction,
                    sourceSystem,
                    item.SourceMessageId,
                    cancellationToken);
                if (existing is null)
                {
                    await InsertEventAsync(
                        connection,
                        transaction,
                        sourceSystem,
                        item,
                        cancellationToken);
                    inserted++;
                }
                else if (Matches(existing, item))
                {
                    duplicates++;
                }
                else
                {
                    throw new SourceIdentityCollisionException(item.SourceMessageId);
                }
            }

            var currentWatermark = existingCheckpoint?.WatermarkSourceTime;
            var advances = currentWatermark is null || watermarkSourceTime > currentWatermark.Value;
            var effectiveWatermark = advances
                ? watermarkSourceTime
                : currentWatermark.GetValueOrDefault();
            var effectiveLastMessageId = advances
                ? lastProcessedMessageId
                : existingCheckpoint!.LastProcessedMessageId;
            await UpsertCheckpointAsync(
                connection,
                transaction,
                sourceSystem,
                streamName,
                effectiveWatermark,
                effectiveLastMessageId,
                completedAtUtc,
                null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AlarmEventIngestionResult(inserted, duplicates);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure; disposal also rolls back an open transaction.
            }

            throw;
        }
    }

    public async Task RecordFailureAsync(
        string sourceSystem,
        string streamName,
        string errorCode,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(sourceSystem, streamName);
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 64)
        {
            throw new ArgumentException("The polling error code must contain 1 to 64 characters.", nameof(errorCode));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var checkpoint = await ReadCheckpointForUpdateAsync(
                connection,
                transaction,
                sourceSystem,
                streamName,
                cancellationToken);
            await UpsertCheckpointAsync(
                connection,
                transaction,
                sourceSystem,
                streamName,
                checkpoint?.WatermarkSourceTime,
                checkpoint?.LastProcessedMessageId,
                checkpoint?.LastCompletedAtUtc,
                errorCode,
                cancellationToken,
                failedAtUtc);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
            }

            throw;
        }
    }

    private async Task<PollingCheckpoint?> ReadCheckpointForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceSystem,
        string streamName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                SourceSystem, StreamName, WatermarkSourceTime,
                LastProcessedMessageId, LastCompletedAtUtc, LastErrorCode
            FROM alarm.PollingCheckpoints WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceSystem = @sourceSystem AND StreamName = @streamName;
            """;
        command.CommandTimeout = _commandTimeoutSeconds;
        AddKeyParameters(command, sourceSystem, streamName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PollingCheckpoint(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private async Task<ExistingAlarmEvent?> ReadExistingEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceSystem,
        Guid sourceMessageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                OriginalMessageType, EventType, SourceNode, OperatorNode, TagName,
                AlarmStatus, AlarmLevel, Area, RawValue, Description, RawPriority,
                RawUnit, SourceEventTime, SourceTimeZoneId, EventTimeUtc,
                AckMatchStatus, DataQualityFlags
            FROM alarm.AlarmEvents WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceSystem = @sourceSystem AND SourceMessageId = @sourceMessageId;
            """;
        command.CommandTimeout = _commandTimeoutSeconds;
        command.Parameters.Add("@sourceSystem", SqlDbType.NVarChar, 32).Value = sourceSystem;
        command.Parameters.Add("@sourceMessageId", SqlDbType.UniqueIdentifier).Value = sourceMessageId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingAlarmEvent(
            reader.GetString(0),
            reader.GetString(1),
            GetNullableString(reader, 2),
            GetNullableString(reader, 3),
            GetNullableString(reader, 4),
            GetNullableString(reader, 5),
            GetNullableString(reader, 6),
            GetNullableString(reader, 7),
            GetNullableString(reader, 8),
            GetNullableString(reader, 9),
            GetNullableString(reader, 10),
            GetNullableString(reader, 11),
            reader.GetDateTime(12),
            GetNullableString(reader, 13),
            reader.IsDBNull(14) ? null : reader.GetDateTime(14),
            reader.GetString(15),
            GetNullableString(reader, 16));
    }

    private async Task InsertEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceSystem,
        AlarmEventIngestionItem item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO alarm.AlarmEvents
            (
                EventId, SourceSystem, SourceMessageId, OriginalMessageType, EventType,
                SourceNode, OperatorNode, TagName, AlarmStatus, AlarmLevel, Area,
                RawValue, Description, RawPriority, RawUnit, SourceEventTime,
                SourceTimeZoneId, EventTimeUtc, IngestedAtUtc, RelatedAlarmId,
                AckMatchStatus, DataQualityFlags
            )
            VALUES
            (
                @eventId, @sourceSystem, @sourceMessageId, @originalMessageType, @eventType,
                @sourceNode, @operatorNode, @tagName, @alarmStatus, @alarmLevel, @area,
                @rawValue, @description, @rawPriority, @rawUnit, @sourceEventTime,
                @sourceTimeZoneId, @eventTimeUtc, @ingestedAtUtc, NULL,
                @ackMatchStatus, @dataQualityFlags
            );
            """;
        command.CommandTimeout = _commandTimeoutSeconds;
        AddEventParameters(command, sourceSystem, item);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertCheckpointAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceSystem,
        string streamName,
        DateTime? watermark,
        Guid? lastMessageId,
        DateTime? completedAtUtc,
        string? errorCode,
        CancellationToken cancellationToken,
        DateTime? updatedAtUtc = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE alarm.PollingCheckpoints
            SET WatermarkSourceTime = @watermark,
                LastProcessedMessageId = @lastMessageId,
                LastCompletedAtUtc = @completedAtUtc,
                LastErrorCode = @errorCode,
                UpdatedAtUtc = @updatedAtUtc
            WHERE SourceSystem = @sourceSystem AND StreamName = @streamName;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO alarm.PollingCheckpoints
                (
                    SourceSystem, StreamName, WatermarkSourceTime,
                    LastProcessedMessageId, LastCompletedAtUtc, LastErrorCode, UpdatedAtUtc
                )
                VALUES
                (
                    @sourceSystem, @streamName, @watermark,
                    @lastMessageId, @completedAtUtc, @errorCode, @updatedAtUtc
                );
            END;
            """;
        command.CommandTimeout = _commandTimeoutSeconds;
        AddKeyParameters(command, sourceSystem, streamName);
        command.Parameters.Add("@watermark", SqlDbType.DateTime2).Value = DbValue(watermark);
        command.Parameters.Add("@lastMessageId", SqlDbType.UniqueIdentifier).Value = DbValue(lastMessageId);
        command.Parameters.Add("@completedAtUtc", SqlDbType.DateTime2).Value = DbValue(completedAtUtc);
        command.Parameters.Add("@errorCode", SqlDbType.NVarChar, 64).Value = DbValue(errorCode);
        command.Parameters.Add("@updatedAtUtc", SqlDbType.DateTime2).Value =
            updatedAtUtc ?? completedAtUtc ?? DateTime.UtcNow;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEventParameters(
        SqlCommand command,
        string sourceSystem,
        AlarmEventIngestionItem item)
    {
        command.Parameters.Add("@eventId", SqlDbType.UniqueIdentifier).Value = item.EventId;
        command.Parameters.Add("@sourceSystem", SqlDbType.NVarChar, 32).Value = sourceSystem;
        command.Parameters.Add("@sourceMessageId", SqlDbType.UniqueIdentifier).Value = item.SourceMessageId;
        AddString(command, "@originalMessageType", 32, item.OriginalMessageType);
        AddString(
            command,
            "@eventType",
            16,
            item.EventType.ToString().ToUpperInvariant(),
            unicode: false);
        AddString(command, "@sourceNode", 128, item.SourceNode);
        AddString(command, "@operatorNode", 128, item.OperatorNode);
        AddString(command, "@tagName", 256, item.TagName);
        AddString(command, "@alarmStatus", 32, item.AlarmStatus);
        AddString(command, "@alarmLevel", 10, item.AlarmLevel, unicode: false);
        AddString(command, "@area", 256, item.Area);
        AddString(command, "@rawValue", 256, item.RawValue);
        AddString(command, "@description", 2048, item.Description);
        AddString(command, "@rawPriority", 128, item.RawPriority);
        AddString(command, "@rawUnit", 128, item.RawUnit);
        command.Parameters.Add("@sourceEventTime", SqlDbType.DateTime2).Value = item.SourceEventTime;
        AddString(command, "@sourceTimeZoneId", 64, item.SourceTimeZoneId);
        command.Parameters.Add("@eventTimeUtc", SqlDbType.DateTime2).Value = DbValue(item.EventTimeUtc);
        command.Parameters.Add("@ingestedAtUtc", SqlDbType.DateTime2).Value = item.IngestedAtUtc;
        AddString(command, "@ackMatchStatus", 20, item.AckMatchStatus, unicode: false);
        AddString(command, "@dataQualityFlags", 256, item.DataQualityFlags);
    }

    private static void AddString(
        SqlCommand command,
        string name,
        int size,
        string? value,
        bool unicode = true) =>
        command.Parameters.Add(name, unicode ? SqlDbType.NVarChar : SqlDbType.VarChar, size).Value =
            DbValue(value);

    private static void AddKeyParameters(
        SqlCommand command,
        string sourceSystem,
        string streamName)
    {
        command.Parameters.Add("@sourceSystem", SqlDbType.NVarChar, 32).Value = sourceSystem;
        command.Parameters.Add("@streamName", SqlDbType.VarChar, 32).Value = streamName;
    }

    private static object DbValue<T>(T? value) => value is null ? DBNull.Value : value;

    private static string? GetNullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static bool Matches(ExistingAlarmEvent existing, AlarmEventIngestionItem item) =>
        existing.OriginalMessageType == item.OriginalMessageType &&
        existing.EventType == item.EventType.ToString().ToUpperInvariant() &&
        existing.SourceNode == item.SourceNode &&
        existing.OperatorNode == item.OperatorNode &&
        existing.TagName == item.TagName &&
        existing.AlarmStatus == item.AlarmStatus &&
        existing.AlarmLevel == item.AlarmLevel &&
        existing.Area == item.Area &&
        existing.RawValue == item.RawValue &&
        existing.Description == item.Description &&
        existing.RawPriority == item.RawPriority &&
        existing.RawUnit == item.RawUnit &&
        existing.SourceEventTime == item.SourceEventTime &&
        existing.SourceTimeZoneId == item.SourceTimeZoneId &&
        existing.EventTimeUtc == item.EventTimeUtc &&
        existing.AckMatchStatus == item.AckMatchStatus &&
        existing.DataQualityFlags == item.DataQualityFlags;

    private static void ValidateKey(string sourceSystem, string streamName)
    {
        if (!string.Equals(sourceSystem, AlarmPollingService.SourceSystem, StringComparison.Ordinal))
        {
            throw new ArgumentException("Stage 2A only supports the iFIX source system.", nameof(sourceSystem));
        }

        _ = AlarmPollingStreams.GetMessageType(streamName);
    }

    private sealed record ExistingAlarmEvent(
        string OriginalMessageType,
        string EventType,
        string? SourceNode,
        string? OperatorNode,
        string? TagName,
        string? AlarmStatus,
        string? AlarmLevel,
        string? Area,
        string? RawValue,
        string? Description,
        string? RawPriority,
        string? RawUnit,
        DateTime SourceEventTime,
        string? SourceTimeZoneId,
        DateTime? EventTimeUtc,
        string AckMatchStatus,
        string? DataQualityFlags);
}
