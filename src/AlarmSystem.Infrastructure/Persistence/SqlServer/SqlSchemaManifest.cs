using System.Text;
using AlarmSystem.Application.Abstractions.Persistence;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

internal sealed record ExpectedColumn(
    string TableName,
    string ColumnName,
    string TypeName,
    short? MaxLength,
    byte? Scale,
    bool IsNullable);

internal sealed record ExpectedIndex(
    string TableName,
    string IndexName,
    bool IsUnique,
    bool IsPrimaryKey,
    string? FilterDefinition,
    params string[] KeyColumns);

internal sealed record ExpectedCheck(
    string TableName,
    string ConstraintName,
    params string[] RequiredDefinitionTokens);

internal sealed record ExpectedForeignKey(
    string TableName,
    string ConstraintName,
    string ColumnName,
    string ReferencedTable,
    string ReferencedColumn);

internal static class SqlSchemaManifest
{
    private static readonly string[] Tables =
    [
        "alarm.AlarmEvents",
        "alarm.AlarmStates",
        "alarm.PollingCheckpoints",
        "alarm.SchemaMigrations",
    ];

    private static readonly ExpectedColumn[] Columns =
    [
        C("AlarmEvents", "EventId", "uniqueidentifier", false),
        C("AlarmEvents", "SourceSystem", "nvarchar", false, 64),
        C("AlarmEvents", "SourceMessageId", "uniqueidentifier", true),
        C("AlarmEvents", "SourceFingerprint", "binary", true, 32),
        C("AlarmEvents", "OriginalMessageType", "nvarchar", false, 64),
        C("AlarmEvents", "EventType", "varchar", false, 16),
        C("AlarmEvents", "SourceNode", "nvarchar", true, 256),
        C("AlarmEvents", "OperatorNode", "nvarchar", true, 256),
        C("AlarmEvents", "TagName", "nvarchar", true, 512),
        C("AlarmEvents", "AlarmStatus", "nvarchar", true, 64),
        C("AlarmEvents", "AlarmLevel", "varchar", true, 10),
        C("AlarmEvents", "Area", "nvarchar", true, 512),
        C("AlarmEvents", "RawValue", "nvarchar", true, 512),
        C("AlarmEvents", "Description", "nvarchar", true, 4096),
        C("AlarmEvents", "SourceEventTime", "datetime2", false, scale: 7),
        C("AlarmEvents", "SourceTimeZoneId", "nvarchar", true, 128),
        C("AlarmEvents", "EventTimeUtc", "datetime2", true, scale: 7),
        C("AlarmEvents", "IngestedAtUtc", "datetime2", false, scale: 7),
        C("AlarmEvents", "RelatedAlarmId", "uniqueidentifier", true),
        C("AlarmEvents", "AckMatchStatus", "varchar", false, 20),

        C("AlarmStates", "AlarmId", "uniqueidentifier", false),
        C("AlarmStates", "SourceSystem", "nvarchar", false, 64),
        C("AlarmStates", "SourceNode", "nvarchar", true, 256),
        C("AlarmStates", "TagName", "nvarchar", true, 512),
        C("AlarmStates", "AlarmStatus", "nvarchar", false, 64),
        C("AlarmStates", "AlarmLevel", "varchar", true, 10),
        C("AlarmStates", "Area", "nvarchar", true, 512),
        C("AlarmStates", "CurrentValue", "nvarchar", true, 512),
        C("AlarmStates", "NormalizedValue", "nvarchar", true, 512),
        C("AlarmStates", "AlarmVersion", "int", false),
        C("AlarmStates", "FirstEventId", "uniqueidentifier", false),
        C("AlarmStates", "CurrentEventId", "uniqueidentifier", false),
        C("AlarmStates", "FirstAlarmSourceTime", "datetime2", false, scale: 7),
        C("AlarmStates", "VersionSourceTime", "datetime2", false, scale: 7),
        C("AlarmStates", "LastAlarmTimeUtc", "datetime2", true, scale: 7),
        C("AlarmStates", "VersionStartedAtUtc", "datetime2", true, scale: 7),
        C("AlarmStates", "IsAcknowledged", "bit", false),
        C("AlarmStates", "AcknowledgedAtUtc", "datetime2", true, scale: 7),
        C("AlarmStates", "AckSourceMessageId", "uniqueidentifier", true),
        C("AlarmStates", "ReminderCount", "int", false),
        C("AlarmStates", "MaxReminders", "int", true),
        C("AlarmStates", "LastNotificationAtUtc", "datetime2", true, scale: 7),
        C("AlarmStates", "NextReminderAtUtc", "datetime2", true, scale: 7),
        C("AlarmStates", "IsCurrent", "bit", false),
        C("AlarmStates", "UpdatedAtUtc", "datetime2", false, scale: 7),
        C("AlarmStates", "RowVersion", "timestamp", false, 8),

        C("PollingCheckpoints", "SourceSystem", "nvarchar", false, 64),
        C("PollingCheckpoints", "StreamName", "varchar", false, 32),
        C("PollingCheckpoints", "WatermarkSourceTime", "datetime2", true, scale: 7),
        C("PollingCheckpoints", "LastProcessedMessageId", "uniqueidentifier", true),
        C("PollingCheckpoints", "LastCompletedAtUtc", "datetime2", true, scale: 7),
        C("PollingCheckpoints", "LastErrorCode", "nvarchar", true, 128),
        C("PollingCheckpoints", "UpdatedAtUtc", "datetime2", false, scale: 7),
        C("PollingCheckpoints", "RowVersion", "timestamp", false, 8),

        C("SchemaMigrations", "MigrationId", "nvarchar", false, 256),
        C("SchemaMigrations", "ChecksumSha256", "char", false, 64),
        C("SchemaMigrations", "AppliedAtUtc", "datetime2", false, scale: 7),
        C("SchemaMigrations", "ToolVersion", "nvarchar", false, 128),
    ];

    private static readonly ExpectedIndex[] Indexes =
    [
        I("AlarmEvents", "PK_AlarmEvents", true, true, null, "EventId"),
        I("AlarmEvents", "UX_AlarmEvents_SourceMessageId", true, false,
            "SourceMessageId IS NOT NULL", "SourceSystem", "SourceMessageId"),
        I("AlarmEvents", "UX_AlarmEvents_SourceFingerprint", true, false,
            "SourceFingerprint IS NOT NULL", "SourceSystem", "SourceFingerprint"),
        I("AlarmEvents", "IX_AlarmEvents_SourceEventTime_EventId", false, false,
            null, "SourceEventTime", "EventId"),
        I("AlarmEvents", "IX_AlarmEvents_EventTimeUtc", false, false,
            "EventTimeUtc IS NOT NULL", "EventTimeUtc"),
        I("AlarmEvents", "IX_AlarmEvents_RelatedAlarmId", false, false,
            "RelatedAlarmId IS NOT NULL", "RelatedAlarmId"),
        I("AlarmStates", "PK_AlarmStates", true, true, null, "AlarmId"),
        I("AlarmStates", "IX_AlarmStates_SourceTagStatus", false, false, null,
            "SourceSystem", "SourceNode", "TagName", "AlarmStatus"),
        I("AlarmStates", "IX_AlarmStates_CurrentReminder", false, false, null,
            "IsCurrent", "IsAcknowledged", "NextReminderAtUtc"),
        I("PollingCheckpoints", "PK_PollingCheckpoints", true, true, null,
            "SourceSystem", "StreamName"),
        I("SchemaMigrations", "PK_SchemaMigrations", true, true, null, "MigrationId"),
    ];

    private static readonly ExpectedCheck[] Checks =
    [
        K("AlarmEvents", "CK_AlarmEvents_EventType", "EventType", "ALARM", "ACK"),
        K("AlarmEvents", "CK_AlarmEvents_AlarmLevel", "AlarmLevel", "HIGH", "MEDIUM", "LOW"),
        K("AlarmEvents", "CK_AlarmEvents_AckMatchStatus", "AckMatchStatus",
            "NotApplicable", "Matched", "Unmatched", "Ambiguous"),
        K("AlarmStates", "CK_AlarmStates_AlarmLevel", "AlarmLevel", "HIGH", "MEDIUM", "LOW"),
        K("AlarmStates", "CK_AlarmStates_AlarmVersion", "AlarmVersion>=1"),
        K("AlarmStates", "CK_AlarmStates_ReminderCount", "ReminderCount>=0"),
        K("AlarmStates", "CK_AlarmStates_MaxReminders", "MaxReminders>=0"),
        K("PollingCheckpoints", "CK_PollingCheckpoints_StreamName",
            "StreamName", "ALARM", "ACK_OPERATOR"),
    ];

    private static readonly ExpectedForeignKey[] ForeignKeys =
    [
        new("alarm.AlarmStates", "FK_AlarmStates_FirstEvent", "FirstEventId",
            "alarm.AlarmEvents", "EventId"),
        new("alarm.AlarmStates", "FK_AlarmStates_CurrentEvent", "CurrentEventId",
            "alarm.AlarmEvents", "EventId"),
    ];

    public static IEnumerable<DatabaseSchemaIssue> Validate(SqlSchemaSnapshot snapshot)
    {
        if (!snapshot.Schemas.Contains("alarm"))
        {
            yield return Missing("alarm", "Required schema is missing.");
        }

        foreach (var table in Tables.Where(table => !snapshot.Tables.Contains(table)))
        {
            yield return Missing(table, "Required table is missing.");
        }

        foreach (var expected in Columns)
        {
            var objectName = $"{expected.TableName}.{expected.ColumnName}";
            if (!snapshot.Columns.TryGetValue(objectName, out var actual))
            {
                yield return Missing(objectName, "Required column is missing.");
                continue;
            }

            if (!string.Equals(expected.TypeName, actual.TypeName, StringComparison.OrdinalIgnoreCase) ||
                expected.MaxLength is not null && expected.MaxLength != actual.MaxLength ||
                expected.Scale is not null && expected.Scale != actual.Scale ||
                expected.IsNullable != actual.IsNullable)
            {
                yield return Unexpected(
                    objectName,
                    $"Expected {Describe(expected)} but found {Describe(actual)}.");
            }
        }

        foreach (var expected in Indexes)
        {
            var objectName = $"{expected.TableName}.{expected.IndexName}";
            if (!snapshot.Indexes.TryGetValue(objectName, out var actual))
            {
                yield return Missing(objectName, "Required index or primary key is missing.");
                continue;
            }

            if (expected.IsUnique != actual.IsUnique ||
                expected.IsPrimaryKey != actual.IsPrimaryKey ||
                !expected.KeyColumns.SequenceEqual(actual.KeyColumns, StringComparer.OrdinalIgnoreCase) ||
                !string.Equals(
                    NormalizeSql(expected.FilterDefinition),
                    NormalizeSql(actual.FilterDefinition),
                    StringComparison.OrdinalIgnoreCase))
            {
                yield return Unexpected(objectName, "Index uniqueness, key order, filter, or PK flag differs.");
            }
        }

        foreach (var expected in Checks)
        {
            var objectName = $"{expected.TableName}.{expected.ConstraintName}";
            if (!snapshot.Checks.TryGetValue(objectName, out var actual))
            {
                yield return Missing(objectName, "Required CHECK constraint is missing.");
                continue;
            }

            var definition = NormalizeSql(actual.Definition);
            if (actual.IsDisabled || actual.IsNotTrusted ||
                expected.RequiredDefinitionTokens.Any(token =>
                !definition.Contains(NormalizeSql(token), StringComparison.OrdinalIgnoreCase)))
            {
                yield return Unexpected(
                    objectName,
                    "CHECK constraint definition differs, is disabled, or is not trusted.");
            }
        }

        foreach (var expected in ForeignKeys)
        {
            var objectName = $"{expected.TableName}.{expected.ConstraintName}";
            if (!snapshot.ForeignKeys.TryGetValue(objectName, out var actual))
            {
                yield return Missing(objectName, "Required foreign key is missing.");
                continue;
            }

            if (!string.Equals(expected.ColumnName, actual.ColumnName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expected.ReferencedTable, actual.ReferencedTable, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expected.ReferencedColumn, actual.ReferencedColumn, StringComparison.OrdinalIgnoreCase) ||
                actual.IsDisabled ||
                actual.IsNotTrusted)
            {
                yield return Unexpected(
                    objectName,
                    "Foreign key mapping differs, is disabled, or is not trusted.");
            }
        }
    }

    private static ExpectedColumn C(
        string table,
        string column,
        string type,
        bool nullable,
        short? maxLength = null,
        byte? scale = null) =>
        new($"alarm.{table}", column, type, maxLength, scale, nullable);

    private static ExpectedIndex I(
        string table,
        string name,
        bool unique,
        bool primary,
        string? filter,
        params string[] columns) =>
        new($"alarm.{table}", name, unique, primary, filter, columns);

    private static ExpectedCheck K(
        string table,
        string name,
        params string[] tokens) =>
        new($"alarm.{table}", name, tokens);

    private static DatabaseSchemaIssue Missing(string name, string description) =>
        new(DatabaseSchemaIssueKind.MissingObject, name, description);

    private static DatabaseSchemaIssue Unexpected(string name, string description) =>
        new(DatabaseSchemaIssueKind.UnexpectedDefinition, name, description);

    private static string Describe(ExpectedColumn column) =>
        $"{column.TypeName}{DescribeLength(column.MaxLength)}{DescribeScale(column.Scale)} " +
        (column.IsNullable ? "NULL" : "NOT NULL");

    private static string Describe(SqlColumnSnapshot column) =>
        $"{column.TypeName}{DescribeLength(column.MaxLength)}{DescribeScale(column.Scale)} " +
        (column.IsNullable ? "NULL" : "NOT NULL");

    private static string DescribeLength(short? length) =>
        length is null ? string.Empty : $"({length})";

    private static string DescribeScale(byte? scale) =>
        scale is null ? string.Empty : $" scale {scale}";

    private static string NormalizeSql(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '<' or '>' or '=')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
