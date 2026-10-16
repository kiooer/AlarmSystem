using AlarmSystem.Application.Abstractions.Persistence;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed class SqlDatabaseSchemaValidator : IDatabaseSchemaValidator
{
    private readonly SqlServerConnectionOptions _options;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IReadOnlyList<SqlMigration> _migrations;

    public SqlDatabaseSchemaValidator(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory)
    {
        if (options.Role != SqlServerConnectionRole.AlarmStore)
        {
            throw new SqlConfigurationException(
                "Database schema validation requires a SqlServer.Role of AlarmStore.");
        }

        _options = options;
        _connectionFactory = connectionFactory;
        _migrations = SqlMigrationCatalog.Load();
    }

    public async Task<DatabaseSchemaValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var target = await DatabaseTargetReader.ReadAsync(connection, null, cancellationToken);
        if (!string.Equals(_options.Database, target.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The connected DB_NAME() does not match SqlServer.Database. Validation was refused.");
        }

        var snapshot = await SqlSchemaSnapshot.ReadAsync(connection, cancellationToken);
        var issues = SqlSchemaManifest.Validate(snapshot).ToList();
        await ValidateMigrationsAsync(connection, issues, cancellationToken);

        return new DatabaseSchemaValidationResult(
            issues.Count == 0,
            _options.Server,
            target.ActualServer,
            target.DatabaseName,
            issues);
    }

    private async Task ValidateMigrationsAsync(
        SqlConnection connection,
        ICollection<DatabaseSchemaIssue> issues,
        CancellationToken cancellationToken)
    {
        var applied = await SqlDatabaseInitializer.ReadAppliedMigrationsAsync(
            connection,
            null,
            cancellationToken);
        foreach (var migration in _migrations)
        {
            if (!applied.TryGetValue(migration.MigrationId, out var checksum))
            {
                issues.Add(new DatabaseSchemaIssue(
                    DatabaseSchemaIssueKind.UnappliedMigration,
                    migration.MigrationId,
                    "The versioned migration has not been applied."));
            }
            else if (!string.Equals(checksum, migration.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new DatabaseSchemaIssue(
                    DatabaseSchemaIssueKind.MigrationChecksumMismatch,
                    migration.MigrationId,
                    "The stored checksum does not match the embedded migration."));
            }
        }

        var knownMigrationIds = _migrations
            .Select(migration => migration.MigrationId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var migrationId in applied.Keys.Where(id => !knownMigrationIds.Contains(id)))
        {
            issues.Add(new DatabaseSchemaIssue(
                DatabaseSchemaIssueKind.UnexpectedDefinition,
                migrationId,
                "The database contains a migration unknown to this tool version."));
        }
    }
}

internal sealed record SqlColumnSnapshot(
    string TableName,
    string ColumnName,
    string TypeName,
    short MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable);

internal sealed record SqlIndexSnapshot(
    string TableName,
    string IndexName,
    bool IsUnique,
    bool IsPrimaryKey,
    string? FilterDefinition,
    IReadOnlyList<string> KeyColumns);

internal sealed record SqlCheckSnapshot(
    string TableName,
    string ConstraintName,
    string Definition,
    bool IsDisabled,
    bool IsNotTrusted);

internal sealed record SqlForeignKeySnapshot(
    string TableName,
    string ConstraintName,
    string ColumnName,
    string ReferencedTable,
    string ReferencedColumn,
    bool IsDisabled,
    bool IsNotTrusted);

internal sealed class SqlSchemaSnapshot
{
    public HashSet<string> Schemas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SqlColumnSnapshot> Columns { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SqlIndexSnapshot> Indexes { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SqlCheckSnapshot> Checks { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SqlForeignKeySnapshot> ForeignKeys { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<SqlSchemaSnapshot> ReadAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var snapshot = new SqlSchemaSnapshot();
        await snapshot.ReadColumnsAsync(connection, cancellationToken);
        await snapshot.ReadIndexesAsync(connection, cancellationToken);
        await snapshot.ReadChecksAsync(connection, cancellationToken);
        await snapshot.ReadForeignKeysAsync(connection, cancellationToken);
        return snapshot;
    }

    private async Task ReadColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.name,
                t.name,
                c.name,
                ty.name,
                c.max_length,
                c.precision,
                c.scale,
                c.is_nullable
            FROM sys.schemas AS s
            LEFT JOIN sys.tables AS t ON t.schema_id = s.schema_id
            LEFT JOIN sys.columns AS c ON c.object_id = t.object_id
            LEFT JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = N'alarm';
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Schemas.Add(reader.GetString(0));
            if (reader.IsDBNull(1))
            {
                continue;
            }

            var table = $"alarm.{reader.GetString(1)}";
            Tables.Add(table);
            if (reader.IsDBNull(2))
            {
                continue;
            }

            var column = new SqlColumnSnapshot(
                table,
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt16(4),
                reader.GetByte(5),
                reader.GetByte(6),
                reader.GetBoolean(7));
            Columns[$"{table}.{column.ColumnName}"] = column;
        }
    }

    private async Task ReadIndexesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.name,
                t.name,
                i.name,
                i.is_unique,
                i.is_primary_key,
                i.filter_definition,
                c.name,
                ic.key_ordinal
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            LEFT JOIN sys.index_columns AS ic
                ON ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
            LEFT JOIN sys.columns AS c
                ON c.object_id = ic.object_id
                AND c.column_id = ic.column_id
            WHERE s.name = N'alarm' AND i.name IS NOT NULL
            ORDER BY s.name, t.name, i.name, ic.key_ordinal;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var builders = new Dictionary<string, IndexBuilder>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = $"{reader.GetString(0)}.{reader.GetString(1)}";
            var name = reader.GetString(2);
            var key = $"{table}.{name}";
            if (!builders.TryGetValue(key, out var builder))
            {
                builder = new IndexBuilder(
                    table,
                    name,
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5));
                builders.Add(key, builder);
            }

            if (!reader.IsDBNull(6))
            {
                builder.KeyColumns.Add(reader.GetString(6));
            }
        }

        foreach (var (key, builder) in builders)
        {
            Indexes[key] = new SqlIndexSnapshot(
                builder.TableName,
                builder.IndexName,
                builder.IsUnique,
                builder.IsPrimaryKey,
                builder.FilterDefinition,
                builder.KeyColumns);
        }
    }

    private async Task ReadChecksAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name, t.name, cc.name, cc.definition, cc.is_disabled, cc.is_not_trusted
            FROM sys.check_constraints AS cc
            INNER JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'alarm';
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = $"{reader.GetString(0)}.{reader.GetString(1)}";
            var check = new SqlCheckSnapshot(
                table,
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5));
            Checks[$"{table}.{check.ConstraintName}"] = check;
        }
    }

    private async Task ReadForeignKeysAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ps.name,
                pt.name,
                fk.name,
                pc.name,
                rs.name,
                rt.name,
                rc.name,
                fk.is_disabled,
                fk.is_not_trusted
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc
                ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.columns AS pc
                ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables AS rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas AS rs ON rs.schema_id = rt.schema_id
            INNER JOIN sys.columns AS rc
                ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            WHERE ps.name = N'alarm';
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = $"{reader.GetString(0)}.{reader.GetString(1)}";
            var foreignKey = new SqlForeignKeySnapshot(
                table,
                reader.GetString(2),
                reader.GetString(3),
                $"{reader.GetString(4)}.{reader.GetString(5)}",
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8));
            ForeignKeys[$"{table}.{foreignKey.ConstraintName}"] = foreignKey;
        }
    }

    private sealed record IndexBuilder(
        string TableName,
        string IndexName,
        bool IsUnique,
        bool IsPrimaryKey,
        string? FilterDefinition)
    {
        public List<string> KeyColumns { get; } = [];
    }
}
