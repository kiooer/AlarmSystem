using AlarmSystem.Application.Polling;
using AlarmSystem.Infrastructure.Persistence.SqlServer;

namespace AlarmSystem.Infrastructure.Sources.IFix;

public sealed class SqlOperatorReader : SqlIFixSourceReader
{
    public SqlOperatorReader(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory,
        IFixAlarmMapper mapper,
        int commandTimeoutSeconds)
        : base(
            AlarmPollingStreams.AckOperator,
            "OPERATOR",
            options,
            connectionFactory,
            mapper,
            commandTimeoutSeconds)
    {
    }
}
