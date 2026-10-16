namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public static class InitializationSafety
{
    public static void ValidateApplyConfirmation(
        SqlServerConnectionOptions options,
        string? confirmedServer,
        string? confirmedDatabase,
        string actualDatabase)
    {
        if (!string.Equals(confirmedServer, options.Server, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--confirm-server must exactly match SqlServer.Server from the selected JSON file.");
        }

        if (!string.Equals(confirmedDatabase, options.Database, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(confirmedDatabase, actualDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--confirm-database must match both SqlServer.Database and DB_NAME() on the connected target.");
        }
    }
}
