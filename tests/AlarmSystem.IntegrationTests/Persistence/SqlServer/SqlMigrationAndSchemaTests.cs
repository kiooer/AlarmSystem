using System.Text;
using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Infrastructure.Persistence.SqlServer;

namespace AlarmSystem.IntegrationTests.Persistence.SqlServer;

public sealed class SqlMigrationAndSchemaTests
{
    [Fact]
    public void DatabaseToolArguments_DefaultInitializeToDryRunAndProtectApply()
    {
        Assert.True(DatabaseToolArguments.TryParse(
            ["initialize", "--config", "config/sql-connection.alarmstore.local.json"],
            out var dryRun,
            out var dryRunError));
        Assert.Null(dryRunError);
        Assert.NotNull(dryRun);
        Assert.False(dryRun.Apply);

        Assert.False(DatabaseToolArguments.TryParse(
            ["initialize", "--config", "config/sql-connection.alarmstore.local.json", "--apply"],
            out _,
            out var applyError));
        Assert.Contains("requires", applyError, StringComparison.OrdinalIgnoreCase);

        Assert.True(DatabaseToolArguments.TryParse(
            [
                "initialize",
                "--config",
                "config/sql-connection.alarmstore.local.json",
                "--apply",
                "--confirm-server",
                @"IFIX-PC\SQLEXPRESS",
                "--confirm-database",
                "ALARM_SYSTEM",
            ],
            out var apply,
            out var confirmedError));
        Assert.Null(confirmedError);
        Assert.NotNull(apply);
        Assert.True(apply.Apply);
    }

    [Fact]
    public void MigrationCatalog_LoadsVersionedFoundationWithStableChecksum()
    {
        var migration = Assert.Single(SqlMigrationCatalog.Load());

        Assert.Equal("0001_CreateAlarmFoundation", migration.MigrationId);
        Assert.Equal(64, migration.ChecksumSha256.Length);
        Assert.Equal(
            SqlMigrationCatalog.ComputeChecksum(Encoding.UTF8.GetBytes(migration.Sql)),
            migration.ChecksumSha256);
    }

    [Fact]
    public void FoundationMigration_DoesNotContainDestructiveOrSourceTableStatements()
    {
        var sql = Assert.Single(SqlMigrationCatalog.Load()).Sql;

        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FIXALARMS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE alarm.AlarmEvents", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE alarm.AlarmStates", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE alarm.PollingCheckpoints", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE alarm.SchemaMigrations", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaManifest_ReportsMissingSchemaTablesColumnsIndexesAndMigrations()
    {
        var issues = SqlSchemaManifest.Validate(new SqlSchemaSnapshot()).ToArray();

        Assert.Contains(issues, issue =>
            issue.Kind == DatabaseSchemaIssueKind.MissingObject && issue.ObjectName == "alarm");
        Assert.Contains(issues, issue => issue.ObjectName == "alarm.AlarmEvents");
        Assert.Contains(issues, issue => issue.ObjectName == "alarm.AlarmStates.AlarmVersion");
        Assert.Contains(issues, issue => issue.ObjectName == "alarm.AlarmStates.CK_AlarmStates_ReminderCount");
        Assert.Contains(issues, issue => issue.ObjectName == "alarm.AlarmStates.FK_AlarmStates_FirstEvent");
    }

    [Fact]
    public void ApplyConfirmation_RequiresExactConfiguredServerAndActualDatabase()
    {
        var options = SqlServerConnectionOptionsTests.ValidOptions(
            role: SqlServerConnectionRole.AlarmStore,
            database: "ALARM_SYSTEM");

        InitializationSafety.ValidateApplyConfirmation(
            options,
            options.Server,
            options.Database,
            options.Database);

        Assert.Throws<InvalidOperationException>(() =>
            InitializationSafety.ValidateApplyConfirmation(
                options,
                "another-server",
                options.Database,
                options.Database));
        Assert.Throws<InvalidOperationException>(() =>
            InitializationSafety.ValidateApplyConfirmation(
                options,
                options.Server,
                options.Database,
                "another-database"));
    }

    [Fact]
    public void DatabaseOperations_EnforceConnectionRolesBeforeOpeningSqlConnection()
    {
        var sourceOptions = SqlServerConnectionOptionsTests.ValidOptions();
        var sourceFactory = new SqlServerConnectionFactory(sourceOptions);
        _ = new SqlSourceSchemaInspector(sourceOptions, sourceFactory);

        Assert.Throws<SqlConfigurationException>(() =>
            new SqlDatabaseInitializer(sourceOptions, sourceFactory));
        Assert.Throws<SqlConfigurationException>(() =>
            new SqlDatabaseSchemaValidator(sourceOptions, sourceFactory));

        var storeOptions = SqlServerConnectionOptionsTests.ValidOptions(
            role: SqlServerConnectionRole.AlarmStore,
            database: "ALARM_SYSTEM");
        var storeFactory = new SqlServerConnectionFactory(storeOptions);
        _ = new SqlDatabaseInitializer(storeOptions, storeFactory);
        _ = new SqlDatabaseSchemaValidator(storeOptions, storeFactory);

        Assert.Throws<SqlConfigurationException>(() =>
            new SqlSourceSchemaInspector(storeOptions, storeFactory));
    }

    [Theory]
    [InlineData(-2, SqlConnectionErrorCategory.Timeout)]
    [InlineData(18456, SqlConnectionErrorCategory.Authentication)]
    [InlineData(4060, SqlConnectionErrorCategory.DatabaseUnavailable)]
    [InlineData(229, SqlConnectionErrorCategory.PermissionDenied)]
    [InlineData(26, SqlConnectionErrorCategory.NamedInstanceResolution)]
    [InlineData(53, SqlConnectionErrorCategory.TcpConnection)]
    [InlineData(11001, SqlConnectionErrorCategory.HostNameResolution)]
    public void ErrorClassifier_MapsKnownSqlErrorNumbers(
        int number,
        SqlConnectionErrorCategory expected)
    {
        Assert.Equal(expected, SqlConnectionErrorClassifier.ClassifySqlErrorNumber(number));
    }
}
