using System.Data.Common;
using AlarmSystem.Application.Polling;

namespace AlarmSystem.Infrastructure.Sources.IFix;

public sealed class IFixAlarmMapper
{
    public SourceAlarmEvent Map(DbDataReader reader) => new(
        GetNullableGuid(reader, "ALM_MSGID"),
        GetNullableDateTime(reader, "ALM_NATIVETIMEIN"),
        GetNullableString(reader, "ALM_PHYSLNODE"),
        GetNullableString(reader, "ALM_OPNODE"),
        GetNullableString(reader, "ALM_TAGNAME"),
        GetRequiredString(reader, "ALM_MSGTYPE").Trim(),
        GetNullableString(reader, "ALM_ALMSTATUS"),
        GetNullableString(reader, "ALM_ALMPRIORITY"),
        GetNullableString(reader, "ALM_ALMAREA"),
        GetNullableString(reader, "ALM_VALUE"),
        GetNullableString(reader, "ALM_DESCR"),
        GetNullableString(reader, "ALM_UNIT"));

    private static string GetRequiredString(DbDataReader reader, string name) =>
        GetNullableString(reader, name) ?? string.Empty;

    private static string? GetNullableString(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTime? GetNullableDateTime(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
