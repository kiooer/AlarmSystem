using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Application.Polling;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.IntegrationTests.Persistence.SqlServer;

public sealed class SqlAlarmEventIngestionRepositoryTests
{
    [SqlServerIngestionFact]
    public async Task Repository_CommitsDeduplicatesAndRollsBackCollisionBatch()
    {
        var configPath = RequireEnvironment("ALARMSYSTEM_SQL_TEST_CONFIG");
        var confirmedDatabase = RequireEnvironment("ALARMSYSTEM_SQL_TEST_CONFIRM_DATABASE");
        var options = await SqlServerConnectionOptionsLoader.LoadAsync(configPath);
        EnsureApprovedIsolatedAlarmStore(options, confirmedDatabase);
        var factory = new SqlServerConnectionFactory(options, "AlarmSystem.IngestionIntegrationTest");
        var initializer = new SqlDatabaseInitializer(options, factory);
        await initializer.InitializeAsync(new DatabaseInitializationRequest(
            true,
            options.Server,
            options.Database,
            "ingestion-integration-test"));
        var repository = new SqlAlarmEventIngestionRepository(options, factory, 30);
        var originalCheckpoint = await repository.GetCheckpointAsync(
            AlarmPollingService.SourceSystem,
            AlarmPollingStreams.Alarm);
        var existingMessageId = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff1");
        var rolledBackMessageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var eventIds = new List<Guid>();

        try
        {
            var sourceTime = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Unspecified);
            var first = Item(Guid.NewGuid(), existingMessageId, sourceTime, "10");
            eventIds.Add(first.EventId);
            var inserted = await repository.CommitBatchAsync(
                AlarmPollingService.SourceSystem,
                AlarmPollingStreams.Alarm,
                [first],
                sourceTime,
                existingMessageId,
                DateTime.UtcNow);
            Assert.Equal(new AlarmEventIngestionResult(1, 0), inserted);

            var duplicate = first with { EventId = Guid.NewGuid(), IngestedAtUtc = DateTime.UtcNow.AddSeconds(1) };
            eventIds.Add(duplicate.EventId);
            var deduplicated = await repository.CommitBatchAsync(
                AlarmPollingService.SourceSystem,
                AlarmPollingStreams.Alarm,
                [duplicate],
                sourceTime.AddSeconds(1),
                null,
                DateTime.UtcNow);
            Assert.Equal(new AlarmEventIngestionResult(0, 1), deduplicated);

            var checkpointBeforeCollision = await repository.GetCheckpointAsync(
                AlarmPollingService.SourceSystem,
                AlarmPollingStreams.Alarm);
            var staged = Item(
                Guid.NewGuid(),
                rolledBackMessageId,
                sourceTime.AddSeconds(2),
                "20");
            eventIds.Add(staged.EventId);
            var collision = first with
            {
                EventId = Guid.NewGuid(),
                RawValue = "DIFFERENT",
                SourceEventTime = sourceTime.AddSeconds(2),
            };
            eventIds.Add(collision.EventId);

            await Assert.ThrowsAsync<SourceIdentityCollisionException>(() =>
                repository.CommitBatchAsync(
                    AlarmPollingService.SourceSystem,
                    AlarmPollingStreams.Alarm,
                    [staged, collision],
                    sourceTime.AddSeconds(2),
                    null,
                    DateTime.UtcNow));

            Assert.False(await EventExistsAsync(factory, rolledBackMessageId));
            var checkpointAfterCollision = await repository.GetCheckpointAsync(
                AlarmPollingService.SourceSystem,
                AlarmPollingStreams.Alarm);
            Assert.Equal(checkpointBeforeCollision, checkpointAfterCollision);
        }
        finally
        {
            await CleanupAsync(factory, eventIds, originalCheckpoint);
        }
    }

    private static AlarmEventIngestionItem Item(
        Guid eventId,
        Guid sourceMessageId,
        DateTime sourceTime,
        string rawValue) => new(
            eventId,
            sourceMessageId,
            "ALARM",
            AlarmEventType.Alarm,
            "INTEGRATION-NODE",
            null,
            "INTEGRATION-TAG",
            "ACTIVE",
            null,
            "INTEGRATION-AREA",
            rawValue,
            "Stage 2A integration fixture",
            "UNMAPPED",
            "unit",
            sourceTime,
            "Taipei Standard Time",
            sourceTime.AddHours(-8),
            DateTime.UtcNow,
            "NotApplicable",
            "PriorityMappingPending");

    private static async Task<bool> EventExistsAsync(
        SqlServerConnectionFactory factory,
        Guid sourceMessageId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM alarm.AlarmEvents
            WHERE SourceSystem = N'iFIX' AND SourceMessageId = @sourceMessageId;
            """;
        command.Parameters.AddWithValue("@sourceMessageId", sourceMessageId);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task CleanupAsync(
        SqlServerConnectionFactory factory,
        IReadOnlyCollection<Guid> eventIds,
        PollingCheckpoint? checkpoint)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            foreach (var eventId in eventIds)
            {
                await using var deleteEvent = connection.CreateCommand();
                deleteEvent.Transaction = transaction;
                deleteEvent.CommandText = "DELETE FROM alarm.AlarmEvents WHERE EventId = @eventId;";
                deleteEvent.Parameters.AddWithValue("@eventId", eventId);
                await deleteEvent.ExecuteNonQueryAsync();
            }

            await using var restoreCheckpoint = connection.CreateCommand();
            restoreCheckpoint.Transaction = transaction;
            if (checkpoint is null)
            {
                restoreCheckpoint.CommandText = """
                    DELETE FROM alarm.PollingCheckpoints
                    WHERE SourceSystem = N'iFIX' AND StreamName = 'ALARM';
                    """;
            }
            else
            {
                restoreCheckpoint.CommandText = """
                    UPDATE alarm.PollingCheckpoints
                    SET WatermarkSourceTime = @watermark,
                        LastProcessedMessageId = @lastMessageId,
                        LastCompletedAtUtc = @lastCompletedAtUtc,
                        LastErrorCode = @lastErrorCode,
                        UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE SourceSystem = N'iFIX' AND StreamName = 'ALARM';
                    """;
                restoreCheckpoint.Parameters.AddWithValue(
                    "@watermark",
                    (object?)checkpoint.WatermarkSourceTime ?? DBNull.Value);
                restoreCheckpoint.Parameters.AddWithValue(
                    "@lastMessageId",
                    (object?)checkpoint.LastProcessedMessageId ?? DBNull.Value);
                restoreCheckpoint.Parameters.AddWithValue(
                    "@lastCompletedAtUtc",
                    (object?)checkpoint.LastCompletedAtUtc ?? DBNull.Value);
                restoreCheckpoint.Parameters.AddWithValue(
                    "@lastErrorCode",
                    (object?)checkpoint.LastErrorCode ?? DBNull.Value);
            }

            await restoreCheckpoint.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static void EnsureApprovedIsolatedAlarmStore(
        SqlServerConnectionOptions options,
        string confirmedDatabase)
    {
        if (options.Role != SqlServerConnectionRole.AlarmStore ||
            string.Equals(options.Database, "IFIX-ALARM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Database, "IFIX_ALARM", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(options.Database, confirmedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Ingestion SQL tests require Role=AlarmStore and an explicitly confirmed isolated database that is not IFIX-ALARM/IFIX_ALARM.");
        }
    }

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Required test environment variable '{name}' is missing.");
}
