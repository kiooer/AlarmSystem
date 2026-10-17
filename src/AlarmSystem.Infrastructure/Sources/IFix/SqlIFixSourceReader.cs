using AlarmSystem.Application.Abstractions.Sources;
using AlarmSystem.Application.Polling;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Sources.IFix;

public abstract class SqlIFixSourceReader : IAlarmSourceReader
{
    internal const string SelectColumns = """
        ALM_MSGID,
        ALM_NATIVETIMEIN,
        ALM_PHYSLNODE,
        ALM_OPNODE,
        ALM_TAGNAME,
        ALM_MSGTYPE,
        ALM_ALMSTATUS,
        ALM_ALMPRIORITY,
        ALM_ALMAREA,
        ALM_VALUE,
        ALM_DESCR,
        ALM_UNIT
        """;

    internal const string MissingTimestampQuery = """
        SELECT TOP (1)
            ALM_MSGID,
            ALM_NATIVETIMEIN,
            ALM_PHYSLNODE,
            ALM_OPNODE,
            ALM_TAGNAME,
            ALM_MSGTYPE,
            ALM_ALMSTATUS,
            ALM_ALMPRIORITY,
            ALM_ALMAREA,
            ALM_VALUE,
            ALM_DESCR,
            ALM_UNIT
        FROM dbo.FIXALARMS
        WHERE ALM_MSGTYPE = @messageType
          AND ALM_NATIVETIMEIN IS NULL
        ORDER BY
            CASE WHEN ALM_MSGID IS NULL THEN 1 ELSE 0 END,
            ALM_MSGID;
        """;

    internal const string WindowQuery = """
        SELECT TOP (@fetchSize)
            ALM_MSGID,
            ALM_NATIVETIMEIN,
            ALM_PHYSLNODE,
            ALM_OPNODE,
            ALM_TAGNAME,
            ALM_MSGTYPE,
            ALM_ALMSTATUS,
            ALM_ALMPRIORITY,
            ALM_ALMAREA,
            ALM_VALUE,
            ALM_DESCR,
            ALM_UNIT
        FROM dbo.FIXALARMS
        WHERE ALM_MSGTYPE = @messageType
          AND ALM_NATIVETIMEIN >= @lowerBound
          AND ALM_NATIVETIMEIN < @upperBound
          AND (@afterTimestamp IS NULL OR ALM_NATIVETIMEIN > @afterTimestamp)
        ORDER BY
            ALM_NATIVETIMEIN,
            CASE WHEN ALM_MSGID IS NULL THEN 1 ELSE 0 END,
            ALM_MSGID;
        """;

    private readonly string _messageType;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IFixAlarmMapper _mapper;
    private readonly int _commandTimeoutSeconds;

    protected SqlIFixSourceReader(
        string streamName,
        string messageType,
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory,
        IFixAlarmMapper mapper,
        int commandTimeoutSeconds)
    {
        if (options.Role != SqlServerConnectionRole.Source)
        {
            throw new SqlConfigurationException(
                "iFIX source readers require a SqlServer.Role of Source.");
        }

        if (commandTimeoutSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeoutSeconds),
                "The source SQL command timeout must be between 1 and 120 seconds.");
        }

        StreamName = streamName;
        _messageType = messageType;
        _connectionFactory = connectionFactory;
        _mapper = mapper;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public string StreamName { get; }

    public async Task<AlarmSourceReadResult> ReadAsync(
        AlarmSourceReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxBatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxBatchSize must be positive.");
        }

        if (request.LowerBoundInclusive >= request.UpperBoundExclusive)
        {
            throw new ArgumentException("The source query window must have a positive duration.", nameof(request));
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var missingTimestamp = await ReadMissingTimestampAsync(connection, cancellationToken);
            if (missingTimestamp is not null)
            {
                return new AlarmSourceReadResult([missingTimestamp], false);
            }

            var rows = await ReadWindowAsync(connection, request, cancellationToken);
            if (rows.Count <= request.MaxBatchSize)
            {
                return new AlarmSourceReadResult(rows, false);
            }

            var boundaryTimestamp = rows[request.MaxBatchSize].SourceEventTime!.Value;
            var completeGroups = rows
                .TakeWhile(row => row.SourceEventTime!.Value < boundaryTimestamp)
                .ToArray();
            if (completeGroups.Length == 0)
            {
                return new AlarmSourceReadResult([], true, boundaryTimestamp);
            }

            return new AlarmSourceReadResult(completeGroups, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            throw new AlarmPollingException(
                "source_sql_timeout",
                $"The iFIX source query for stream '{StreamName}' timed out.",
                exception);
        }
        catch (SqlException exception)
        {
            throw new AlarmPollingException(
                "source_sql_error",
                $"The iFIX source query for stream '{StreamName}' failed with a SQL error.",
                exception);
        }
    }

    private async Task<SourceAlarmEvent?> ReadMissingTimestampAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = MissingTimestampQuery;
        command.CommandTimeout = _commandTimeoutSeconds;
        command.Parameters.Add("@messageType", System.Data.SqlDbType.NVarChar, 32).Value = _messageType;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? _mapper.Map(reader) : null;
    }

    private async Task<List<SourceAlarmEvent>> ReadWindowAsync(
        SqlConnection connection,
        AlarmSourceReadRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = WindowQuery;
        command.CommandTimeout = _commandTimeoutSeconds;
        command.Parameters.Add("@fetchSize", System.Data.SqlDbType.Int).Value =
            checked(request.MaxBatchSize + 1);
        command.Parameters.Add("@messageType", System.Data.SqlDbType.NVarChar, 32).Value = _messageType;
        command.Parameters.Add("@lowerBound", System.Data.SqlDbType.DateTime).Value =
            request.LowerBoundInclusive;
        command.Parameters.Add("@upperBound", System.Data.SqlDbType.DateTime).Value =
            request.UpperBoundExclusive;
        command.Parameters.Add("@afterTimestamp", System.Data.SqlDbType.DateTime).Value =
            request.AfterTimestampExclusive is DateTime cursor ? cursor : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SourceAlarmEvent>(request.MaxBatchSize + 1);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(_mapper.Map(reader));
        }

        return rows;
    }
}
