namespace AlarmSystem.IntegrationTests.Persistence.SqlServer;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public const string EnableVariable = "ALARMSYSTEM_RUN_SQL_INTEGRATION_TESTS";

    public SqlServerIntegrationFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(EnableVariable),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Set {EnableVariable}=true and provide an approved isolated test database.";
        }
    }
}
