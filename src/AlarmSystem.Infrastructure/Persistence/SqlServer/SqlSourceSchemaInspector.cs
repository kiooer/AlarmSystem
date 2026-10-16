using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed record SourceColumnInfo(
    string Name,
    string SqlType,
    short MaxLengthBytes,
    byte Precision,
    byte Scale,
    bool IsNullable);

public sealed record SourceIndexInfo(
    string Name,
    bool IsPrimaryKey,
    bool IsUnique,
    IReadOnlyList<string> KeyColumns);

public enum SourceSchemaFindingKind
{
    MissingTable,
    MissingColumn,
    ProvisionalTypeMismatch,
}

public sealed record SourceSchemaFinding(
    SourceSchemaFindingKind Kind,
    string ObjectName,
    string Description);

public sealed record SourceMessageIdStatistics(long NullCount, long DuplicateValueCount);

public sealed record SourceSchemaInspectionResult(
    string TableName,
    IReadOnlyList<SourceColumnInfo> Columns,
    IReadOnlyList<SourceIndexInfo> Indexes,
    IReadOnlyList<SourceSchemaFinding> Findings,
    SourceMessageIdStatistics? MessageIdStatistics);

public sealed class SqlSourceSchemaInspector
{
    private const string SourceTable = "dbo.FIXALARMS";

    private static readonly string[] TextTypes = ["nvarchar", "varchar", "nchar", "char", "ntext", "text"];

    private static readonly Dictionary<string, string[]> ProvisionalTypes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["ALM_NATIVETIMEIN"] = ["datetime", "datetime2", "datetimeoffset", "smalldatetime"],
        ["ALM_PHYSLNODE"] = TextTypes,
        ["ALM_TAGNAME"] = TextTypes,
        ["ALM_DESCR"] = TextTypes,
        ["ALM_VALUE"] = TextTypes,
        ["ALM_UNIT"] = TextTypes,
        ["ALM_MSGTYPE"] = TextTypes,
        ["ALM_ALMSTATUS"] = TextTypes,
        ["ALM_ALMPRIORITY"] = TextTypes,
        ["ALM_ALMAREA"] = TextTypes,
        ["ALM_OPNODE"] = TextTypes,
        ["ALM_MSGID"] = ["uniqueidentifier"],
    };

    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlSourceSchemaInspector(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory)
    {
        if (options.Role != SqlServerConnectionRole.Source)
        {
            throw new SqlConfigurationException(
                "Source schema inspection requires a SqlServer.Role of Source.");
        }

        _connectionFactory = connectionFactory;
    }

    public async Task<SourceSchemaInspectionResult> InspectAsync(
        bool includeMessageIdStatistics = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var columns = await ReadColumnsAsync(connection, cancellationToken);
        if (columns.Count == 0)
        {
            return new SourceSchemaInspectionResult(
                SourceTable,
                columns,
                [],
                [new SourceSchemaFinding(
                    SourceSchemaFindingKind.MissingTable,
                    SourceTable,
                    "The source table is missing or its metadata is not visible to this SQL login.")],
                null);
        }

        var indexes = await ReadIndexesAsync(connection, cancellationToken);
        var findings = CompareColumns(columns);
        SourceMessageIdStatistics? statistics = null;
        if (includeMessageIdStatistics &&
            columns.Any(column => string.Equals(
                column.Name,
                "ALM_MSGID",
                StringComparison.OrdinalIgnoreCase)))
        {
            statistics = await ReadMessageIdStatisticsAsync(connection, cancellationToken);
        }

        return new SourceSchemaInspectionResult(
            SourceTable,
            columns,
            indexes,
            findings,
            statistics);
    }

    private static async Task<List<SourceColumnInfo>> ReadColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable
            FROM sys.columns AS c
            INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.FIXALARMS', N'U')
            ORDER BY c.column_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<SourceColumnInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new SourceColumnInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt16(2),
                reader.GetByte(3),
                reader.GetByte(4),
                reader.GetBoolean(5)));
        }

        return columns;
    }

    private static async Task<List<SourceIndexInfo>> ReadIndexesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, i.is_primary_key, i.is_unique, c.name, ic.key_ordinal
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic
                ON ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
            INNER JOIN sys.columns AS c
                ON c.object_id = ic.object_id
                AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.FIXALARMS', N'U')
              AND i.name IS NOT NULL
            ORDER BY i.index_id, ic.key_ordinal;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var builders = new Dictionary<string, SourceIndexBuilder>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (!builders.TryGetValue(name, out var builder))
            {
                builder = new SourceIndexBuilder(name, reader.GetBoolean(1), reader.GetBoolean(2));
                builders.Add(name, builder);
            }

            builder.Columns.Add(reader.GetString(3));
        }

        return builders.Values
            .Select(builder => new SourceIndexInfo(
                builder.Name,
                builder.IsPrimaryKey,
                builder.IsUnique,
                builder.Columns))
            .ToList();
    }

    private static List<SourceSchemaFinding> CompareColumns(
        IReadOnlyCollection<SourceColumnInfo> columns)
    {
        var actual = columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var findings = new List<SourceSchemaFinding>();
        foreach (var (name, compatibleTypes) in ProvisionalTypes)
        {
            if (!actual.TryGetValue(name, out var column))
            {
                findings.Add(new SourceSchemaFinding(
                    SourceSchemaFindingKind.MissingColumn,
                    $"{SourceTable}.{name}",
                    "The priority source column was not found."));
            }
            else if (!compatibleTypes.Contains(column.SqlType, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(new SourceSchemaFinding(
                    SourceSchemaFindingKind.ProvisionalTypeMismatch,
                    $"{SourceTable}.{name}",
                    $"Actual type '{column.SqlType}' is outside the provisional compatibility set; confirm the iFIX schema before stage 2."));
            }
        }

        return findings;
    }

    private static async Task<SourceMessageIdStatistics> ReadMessageIdStatisticsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.FIXALARMS WHERE ALM_MSGID IS NULL),
                (
                    SELECT COALESCE(SUM(item_count - 1), 0)
                    FROM
                    (
                        SELECT COUNT_BIG(*) AS item_count
                        FROM dbo.FIXALARMS
                        WHERE ALM_MSGID IS NOT NULL
                        GROUP BY ALM_MSGID
                        HAVING COUNT_BIG(*) > 1
                    ) AS duplicate_groups
                );
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SourceMessageIdStatistics(
            reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1));
    }

    private sealed record SourceIndexBuilder(string Name, bool IsPrimaryKey, bool IsUnique)
    {
        public List<string> Columns { get; } = [];
    }
}
