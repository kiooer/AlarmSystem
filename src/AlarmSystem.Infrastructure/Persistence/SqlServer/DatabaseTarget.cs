using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

internal sealed record DatabaseTarget(string ActualServer, string DatabaseName);

internal static class DatabaseTargetReader
{
    public static async Task<DatabaseTarget> ReadAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)),
                DB_NAME();
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("SQL Server did not return target identity.");
        }

        return new DatabaseTarget(reader.GetString(0), reader.GetString(1));
    }
}
