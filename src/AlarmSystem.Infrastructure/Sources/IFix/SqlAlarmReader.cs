using AlarmSystem.Application.Polling;
using AlarmSystem.Infrastructure.Persistence.SqlServer;

namespace AlarmSystem.Infrastructure.Sources.IFix;

public sealed class SqlAlarmReader : SqlIFixSourceReader
{
    public SqlAlarmReader(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory,
        IFixAlarmMapper mapper,
        int commandTimeoutSeconds)
        : base(
            AlarmPollingStreams.Alarm,
            "ALARM",
            options,
            connectionFactory,
            mapper,
            commandTimeoutSeconds)
    {
    }
}
