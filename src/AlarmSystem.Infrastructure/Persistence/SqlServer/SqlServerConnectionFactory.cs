using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed class SqlServerConnectionFactory
{
    private readonly SqlServerConnectionOptions _options;

    public SqlServerConnectionFactory(SqlServerConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public SqlConnection CreateConnection() => new(CreateConnectionStringBuilder().ConnectionString);

    public SqlConnectionStringBuilder CreateConnectionStringBuilder() => new()
    {
        DataSource = _options.Server,
        InitialCatalog = _options.Database,
        Authentication = SqlAuthenticationMethod.SqlPassword,
        IntegratedSecurity = false,
        UserID = _options.UserName,
        Password = _options.Password,
        Encrypt = _options.Encrypt!.Value,
        TrustServerCertificate = _options.TrustServerCertificate!.Value,
        ConnectTimeout = _options.ConnectionTimeoutSeconds,
        PersistSecurityInfo = false,
        ApplicationName = "AlarmSystem.DatabaseTool",
    };
}
