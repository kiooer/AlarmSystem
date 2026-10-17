namespace AlarmSystem.IntegrationTests.Persistence.SqlServer;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerIngestionFactAttribute : FactAttribute
{
    public const string EnableVariable = "ALARMSYSTEM_RUN_SQL_INGESTION_TESTS";

    public SqlServerIngestionFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(EnableVariable),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Set {EnableVariable}=true only for an approved isolated AlarmStore with no running Worker.";
        }
    }
}
