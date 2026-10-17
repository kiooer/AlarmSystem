using System.Data;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using AlarmSystem.Infrastructure.Sources.IFix;

namespace AlarmSystem.IntegrationTests.Sources.IFix;

public sealed class IFixAlarmMapperAndQueryTests
{
    [Fact]
    public void Mapper_UsesConfirmedAreaPriorityAndUnitColumns()
    {
        var id = Guid.NewGuid();
        var time = new DateTime(2026, 10, 17, 12, 34, 56, DateTimeKind.Unspecified);
        using var table = CreateSourceTable();
        table.Rows.Add(
            id,
            time,
            "SOURCE-NODE",
            "OP-NODE",
            DBNull.Value,
            "OPERATOR",
            "STATUS",
            "P-RAW",
            "AREA-CONFIRMED",
            "12.5",
            "ACK-like text",
            "psi");
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var mapped = new IFixAlarmMapper().Map(reader);

        Assert.Equal(id, mapped.SourceMessageId);
        Assert.Equal(time, mapped.SourceEventTime);
        Assert.Equal("AREA-CONFIRMED", mapped.Area);
        Assert.Equal("P-RAW", mapped.RawPriority);
        Assert.Equal("psi", mapped.RawUnit);
        Assert.Equal("OPERATOR", mapped.OriginalMessageType);
        Assert.Null(mapped.TagName);
    }

    [Fact]
    public void ReaderSql_IsExplicitParameterizedBoundedAndDoesNotUseNoLockOrTimeFunctions()
    {
        var sql = SqlIFixSourceReader.WindowQuery;

        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALM_ALMAREA", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALM_AREA", sql, StringComparison.Ordinal);
        Assert.Contains("ALM_MSGTYPE = @messageType", sql, StringComparison.Ordinal);
        Assert.Contains("ALM_NATIVETIMEIN >= @lowerBound", sql, StringComparison.Ordinal);
        Assert.Contains("ALM_NATIVETIMEIN < @upperBound", sql, StringComparison.Ordinal);
        Assert.Contains("TOP (@fetchSize)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("NOLOCK", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONVERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FORMAT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readers_EnforceSourceRoleBeforeAnyConnectionAttempt()
    {
        var source = ValidOptions(SqlServerConnectionRole.Source);
        var store = ValidOptions(SqlServerConnectionRole.AlarmStore);
        var mapper = new IFixAlarmMapper();

        _ = new SqlAlarmReader(source, new SqlServerConnectionFactory(source), mapper, 10);
        _ = new SqlOperatorReader(source, new SqlServerConnectionFactory(source), mapper, 10);
        Assert.Throws<SqlConfigurationException>(() =>
            new SqlAlarmReader(store, new SqlServerConnectionFactory(store), mapper, 10));
    }

    private static DataTable CreateSourceTable()
    {
        var table = new DataTable();
        table.Columns.Add("ALM_MSGID", typeof(Guid));
        table.Columns.Add("ALM_NATIVETIMEIN", typeof(DateTime));
        table.Columns.Add("ALM_PHYSLNODE", typeof(string));
        table.Columns.Add("ALM_OPNODE", typeof(string));
        table.Columns.Add("ALM_TAGNAME", typeof(string));
        table.Columns.Add("ALM_MSGTYPE", typeof(string));
        table.Columns.Add("ALM_ALMSTATUS", typeof(string));
        table.Columns.Add("ALM_ALMPRIORITY", typeof(string));
        table.Columns.Add("ALM_ALMAREA", typeof(string));
        table.Columns.Add("ALM_VALUE", typeof(string));
        table.Columns.Add("ALM_DESCR", typeof(string));
        table.Columns.Add("ALM_UNIT", typeof(string));
        return table;
    }

    private static SqlServerConnectionOptions ValidOptions(SqlServerConnectionRole role) => new()
    {
        Role = role,
        Server = "tcp:example.invalid,1433",
        Database = role == SqlServerConnectionRole.Source ? "IFIX-ALARM" : "ALARM_TEST",
        Authentication = "SqlPassword",
        UserName = "not-a-real-user",
        Password = "not-a-real-password",
        Encrypt = true,
        TrustServerCertificate = false,
        ConnectionTimeoutSeconds = 10,
    };
}
