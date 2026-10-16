using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.IntegrationTests.Persistence.SqlServer;

public sealed class SqlServerDatabaseIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task Initializer_IsIdempotentConcurrentAndDoesNotModifyFixAlarms()
    {
        var configPath = RequireEnvironment("ALARMSYSTEM_SQL_TEST_CONFIG");
        var confirmedDatabase = RequireEnvironment("ALARMSYSTEM_SQL_TEST_CONFIRM_DATABASE");
        var options = await SqlServerConnectionOptionsLoader.LoadAsync(configPath);
        if (options.Role != SqlServerConnectionRole.AlarmStore ||
            string.Equals(options.Database, "IFIX-ALARM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Database, "IFIX_ALARM", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(options.Database, confirmedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SQL integration tests require Role=AlarmStore and an explicitly confirmed isolated database that is not IFIX-ALARM/IFIX_ALARM.");
        }

        var factory = new SqlServerConnectionFactory(options);
        var sourceBefore = await ReadFixAlarmsMetadataAsync(factory);
        var initializer = new SqlDatabaseInitializer(options, factory);
        var request = new DatabaseInitializationRequest(
            true,
            options.Server,
            options.Database,
            "integration-test");

        await Task.WhenAll(
            initializer.InitializeAsync(request),
            initializer.InitializeAsync(request));
        await initializer.InitializeAsync(request);

        var validation = await new SqlDatabaseSchemaValidator(options, factory).ValidateAsync();
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));
        Assert.Equal(sourceBefore, await ReadFixAlarmsMetadataAsync(factory));
        await VerifyEventAndStateConstraintsAsync(factory);
        await VerifyFailedMigrationRollsBackAsync(options, factory);
    }

    private static async Task VerifyEventAndStateConstraintsAsync(
        SqlServerConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var alarmEventId = Guid.NewGuid();
            var ackEventId = Guid.NewGuid();
            await InsertEventAsync(connection, transaction, alarmEventId, Guid.NewGuid(), "ALARM", "tag-1");
            await InsertEventAsync(connection, transaction, ackEventId, Guid.NewGuid(), "ACK", null);
            Assert.NotEqual(alarmEventId, ackEventId);

            await using var invalidState = connection.CreateCommand();
            invalidState.Transaction = transaction;
            invalidState.CommandText = """
                INSERT INTO alarm.AlarmStates
                (
                    AlarmId, SourceSystem, AlarmStatus, AlarmVersion,
                    FirstEventId, CurrentEventId, FirstAlarmSourceTime, VersionSourceTime,
                    IsAcknowledged, ReminderCount, IsCurrent, UpdatedAtUtc
                )
                VALUES
                (
                    NEWID(), N'iFIX', N'ACTIVE', 0,
                    @eventId, @eventId, SYSUTCDATETIME(), SYSUTCDATETIME(),
                    0, -1, 1, SYSUTCDATETIME()
                );
                """;
            invalidState.Parameters.AddWithValue("@eventId", alarmEventId);
            await Assert.ThrowsAsync<SqlException>(() => invalidState.ExecuteNonQueryAsync());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task InsertEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid eventId,
        Guid sourceMessageId,
        string eventType,
        string? tag)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO alarm.AlarmEvents
            (
                EventId, SourceSystem, SourceMessageId, OriginalMessageType,
                EventType, TagName, SourceEventTime, IngestedAtUtc, AckMatchStatus
            )
            VALUES
            (
                @eventId, N'iFIX', @sourceMessageId, @eventType,
                @eventType, @tag, SYSUTCDATETIME(), SYSUTCDATETIME(), @ackMatchStatus
            );
            """;
        command.Parameters.AddWithValue("@eventId", eventId);
        command.Parameters.AddWithValue("@sourceMessageId", sourceMessageId);
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@tag", (object?)tag ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ackMatchStatus",
            eventType == "ACK" ? "Unmatched" : "NotApplicable");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task VerifyFailedMigrationRollsBackAsync(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory factory)
    {
        var migration = new SqlMigration(
            "9999_ForcedRollbackProbe",
            SqlMigrationCatalog.ComputeChecksum("probe"u8),
            "CREATE TABLE alarm.RollbackProbe (Id int NOT NULL); THROW 51000, 'rollback', 1;");
        var initializer = new SqlDatabaseInitializer(options, factory, [migration]);
        var request = new DatabaseInitializationRequest(
            true,
            options.Server,
            options.Database,
            "integration-test");

        await Assert.ThrowsAsync<SqlException>(() => initializer.InitializeAsync(request));

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OBJECT_ID(N'alarm.RollbackProbe', N'U');";
        Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ReadFixAlarmsMetadataAsync(
        SqlServerConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CONCAT(object_id, N'|', CONVERT(nvarchar(33), create_date, 126), N'|',
                CONVERT(nvarchar(33), modify_date, 126))
            FROM sys.tables
            WHERE object_id = OBJECT_ID(N'dbo.FIXALARMS', N'U');
            """;
        return await command.ExecuteScalarAsync() as string;
    }

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required test environment variable '{name}' is missing.");
}
