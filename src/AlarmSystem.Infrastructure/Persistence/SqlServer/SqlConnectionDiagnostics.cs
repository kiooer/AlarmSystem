using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public enum SqlConnectionErrorCategory
{
    None,
    Configuration,
    HostNameResolution,
    TcpConnection,
    NamedInstanceResolution,
    Authentication,
    DatabaseUnavailable,
    TlsCertificate,
    PermissionDenied,
    Timeout,
    Unknown,
}

public sealed record SqlConnectionTestResult(
    bool ConnectionStatus,
    SqlServerConnectionRole Role,
    string TargetServer,
    string? ActualServer,
    string? DatabaseName,
    string? SqlServerVersion,
    string? SqlServerEdition,
    string? InstanceName,
    long ConnectionElapsedMs,
    SqlConnectionErrorCategory ErrorCategory,
    string? Recommendation,
    bool CanInspectSourceSchema);

public sealed class SqlConnectionDiagnostics
{
    private const string TestSql = """
        SELECT
            CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)),
            DB_NAME(),
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
            CAST(SERVERPROPERTY('Edition') AS nvarchar(256)),
            CAST(SERVERPROPERTY('InstanceName') AS nvarchar(128)),
            CASE WHEN
                HAS_PERMS_BY_NAME(N'dbo.FIXALARMS', N'OBJECT', N'VIEW DEFINITION') = 1
                OR HAS_PERMS_BY_NAME(N'dbo.FIXALARMS', N'OBJECT', N'SELECT') = 1
            THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
        """;

    private readonly SqlServerConnectionOptions _options;
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlConnectionDiagnostics(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory connectionFactory)
    {
        _options = options;
        _connectionFactory = connectionFactory;
    }

    public async Task<SqlConnectionTestResult> TestAsync(
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using (var selectOne = connection.CreateCommand())
            {
                selectOne.CommandText = "SELECT 1;";
                _ = await selectOne.ExecuteScalarAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = TestSql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            return new SqlConnectionTestResult(
                true,
                _options.Role,
                _options.Server,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? "default" : reader.GetString(4),
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                SqlConnectionErrorCategory.None,
                null,
                reader.GetBoolean(5));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(started, SqlConnectionErrorCategory.Timeout);
        }
        catch (Exception exception) when (exception is SqlException or SocketException or AuthenticationException)
        {
            return Failure(started, SqlConnectionErrorClassifier.Classify(exception));
        }
    }

    private SqlConnectionTestResult Failure(
        long started,
        SqlConnectionErrorCategory category) => new(
            false,
            _options.Role,
            _options.Server,
            null,
            null,
            null,
            null,
            null,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            category,
            SqlConnectionErrorClassifier.GetRecommendation(category),
            false);
}

public static class SqlConnectionErrorClassifier
{
    public static SqlConnectionErrorCategory Classify(Exception exception)
    {
        if (Contains<AuthenticationException>(exception))
        {
            return SqlConnectionErrorCategory.TlsCertificate;
        }

        if (exception is SocketException socketException)
        {
            return ClassifySocketError(socketException.SocketErrorCode);
        }

        if (exception is SqlException sqlException)
        {
            return ClassifySqlErrorNumber(sqlException.Number, sqlException.InnerException);
        }

        return SqlConnectionErrorCategory.Unknown;
    }

    internal static SqlConnectionErrorCategory ClassifySqlErrorNumber(
        int number,
        Exception? innerException = null) => number switch
        {
            -2 => SqlConnectionErrorCategory.Timeout,
            18456 or 18452 => SqlConnectionErrorCategory.Authentication,
            4060 or 911 => SqlConnectionErrorCategory.DatabaseUnavailable,
            229 or 230 or 262 => SqlConnectionErrorCategory.PermissionDenied,
            26 => SqlConnectionErrorCategory.NamedInstanceResolution,
            53 or 64 or 10060 or 10061 => SqlConnectionErrorCategory.TcpConnection,
            11001 or 11004 => SqlConnectionErrorCategory.HostNameResolution,
            _ when Contains<AuthenticationException>(innerException) =>
                SqlConnectionErrorCategory.TlsCertificate,
            _ => SqlConnectionErrorCategory.Unknown,
        };

    internal static SqlConnectionErrorCategory ClassifySocketError(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.NoData =>
            SqlConnectionErrorCategory.HostNameResolution,
        SocketError.TimedOut => SqlConnectionErrorCategory.Timeout,
        SocketError.ConnectionRefused or SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
            SqlConnectionErrorCategory.TcpConnection,
        _ => SqlConnectionErrorCategory.Unknown,
    };

    public static string GetRecommendation(SqlConnectionErrorCategory category) => category switch
    {
        SqlConnectionErrorCategory.HostNameResolution =>
            "Verify the remote host/FQDN in JSON and DNS resolution from the development PC.",
        SqlConnectionErrorCategory.TcpConnection =>
            "Verify SQL Server TCP/IP, the actual listening port, routing, and the scoped Windows Firewall rule.",
        SqlConnectionErrorCategory.NamedInstanceResolution =>
            "Verify the instance name and SQL Server Browser/UDP 1434, or use tcp:HOST,PORT.",
        SqlConnectionErrorCategory.Authentication =>
            "Verify Mixed Mode, the SQL login, password, and that the login is enabled.",
        SqlConnectionErrorCategory.DatabaseUnavailable =>
            "Verify that the configured database exists and the SQL login can access it.",
        SqlConnectionErrorCategory.TlsCertificate =>
            "Verify the SQL Server TLS certificate chain and host name. Do not disable validation automatically.",
        SqlConnectionErrorCategory.PermissionDenied =>
            "Grant only the required database permissions to the SQL login.",
        SqlConnectionErrorCategory.Timeout =>
            "Verify network reachability and the configured timeout; do not assume TCP 1433.",
        _ => "Review SQL Server and network diagnostics without exposing credentials.",
    };

    private static bool Contains<TException>(Exception? exception)
        where TException : Exception
    {
        while (exception is not null)
        {
            if (exception is TException)
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }
}
