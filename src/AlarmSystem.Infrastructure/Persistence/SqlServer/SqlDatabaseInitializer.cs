using AlarmSystem.Application.Abstractions.Persistence;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed class SqlDatabaseInitializer : IDatabaseInitializer
{
    private const string MigrationTableName = "alarm.SchemaMigrations";
    private const string AcquireLockSql = """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock
            @Resource = N'AlarmSystem.DatabaseInitializer',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 15000;
        SELECT @result;
        """;

    private readonly SqlServerConnectionOptions _options;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IReadOnlyList<SqlMigration> _migrations;

    public SqlDatabaseInitializer(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory)
        : this(options, connectionFactory, SqlMigrationCatalog.Load())
    {
    }

    internal SqlDatabaseInitializer(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory,
        IReadOnlyList<SqlMigration> migrations)
    {
        if (options.Role != SqlServerConnectionRole.AlarmStore)
        {
            throw new SqlConfigurationException(
                "Database initialization requires a SqlServer.Role of AlarmStore.");
        }

        _options = options;
        _connectionFactory = connectionFactory;
        _migrations = migrations;
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(
        DatabaseInitializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ToolVersion) || request.ToolVersion.Length > 64)
        {
            throw new ArgumentException("ToolVersion must contain 1 to 64 characters.", nameof(request));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var target = await DatabaseTargetReader.ReadAsync(connection, null, cancellationToken);
        EnsureConfiguredDatabase(target.DatabaseName);

        if (!request.Apply)
        {
            var applied = await ReadAppliedMigrationsAsync(connection, null, cancellationToken);
            var plan = BuildPlan(applied);
            return CreateResult(false, target, plan);
        }

        InitializationSafety.ValidateApplyConfirmation(
            _options,
            request.ConfirmedServer,
            request.ConfirmedDatabase,
            target.DatabaseName);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireLockAsync(connection, transaction, cancellationToken);
            var applied = await ReadAppliedMigrationsAsync(connection, transaction, cancellationToken);
            var plan = BuildPlan(applied);

            foreach (var migration in _migrations.Where(m => !applied.ContainsKey(m.MigrationId)))
            {
                await ExecuteMigrationAsync(connection, transaction, migration, cancellationToken);
                await RecordMigrationAsync(
                    connection,
                    transaction,
                    migration,
                    request.ToolVersion,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return CreateResult(true, target, plan);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original initialization failure. The uncommitted transaction
                // is also rolled back when the connection is disposed.
            }

            throw;
        }
    }

    private DatabaseInitializationResult CreateResult(
        bool applied,
        DatabaseTarget target,
        IReadOnlyList<DatabaseMigrationPlanItem> plan) => new(
            applied,
            _options.Server,
            target.ActualServer,
            target.DatabaseName,
            plan);

    private void EnsureConfiguredDatabase(string actualDatabase)
    {
        if (!string.Equals(_options.Database, actualDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The connected DB_NAME() does not match SqlServer.Database. Initialization was refused.");
        }
    }

    private IReadOnlyList<DatabaseMigrationPlanItem> BuildPlan(
        IReadOnlyDictionary<string, string> applied)
    {
        foreach (var migration in _migrations)
        {
            if (applied.TryGetValue(migration.MigrationId, out var checksum) &&
                !string.Equals(checksum, migration.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Migration checksum mismatch for '{migration.MigrationId}'. No DDL was executed.");
            }
        }

        return _migrations
            .Select(migration => new DatabaseMigrationPlanItem(
                migration.MigrationId,
                migration.ChecksumSha256,
                applied.ContainsKey(migration.MigrationId)))
            .ToArray();
    }

    private static async Task AcquireLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = AcquireLockSql;
        command.CommandTimeout = 20;
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new InvalidOperationException(
                "The database initializer concurrency lock could not be acquired.");
        }
    }

    internal static async Task<Dictionary<string, string>> ReadAppliedMigrationsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText =
            "SELECT CASE WHEN OBJECT_ID(N'alarm.SchemaMigrations', N'U') IS NULL " +
            "THEN 0 ELSE 1 END;";
        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) == 1;
        if (!exists)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT MigrationId, ChecksumSha256 FROM {MigrationTableName};";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0), reader.GetString(1));
        }

        return applied;
    }

    private static async Task ExecuteMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SqlMigration migration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = migration.Sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SqlMigration migration,
        string toolVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO alarm.SchemaMigrations
                (MigrationId, ChecksumSha256, AppliedAtUtc, ToolVersion)
            VALUES
                (@migrationId, @checksum, SYSUTCDATETIME(), @toolVersion);
            """;
        command.Parameters.AddWithValue("@migrationId", migration.MigrationId);
        command.Parameters.AddWithValue("@checksum", migration.ChecksumSha256);
        command.Parameters.AddWithValue("@toolVersion", toolVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
