using Microsoft.Data.SqlClient;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed class SqlServerConnectionFactory
{
    private readonly SqlServerConnectionOptions _options;
    private readonly string _applicationName;

    public SqlServerConnectionFactory(
        SqlServerConnectionOptions options,
        string applicationName = "AlarmSystem.DatabaseTool")
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (string.IsNullOrWhiteSpace(applicationName) || applicationName.Length > 128)
        {
            throw new ArgumentException(
                "The SQL application name must contain 1 to 128 characters.",
                nameof(applicationName));
        }

        _options = options;
        _applicationName = applicationName;
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
        ApplicationName = _applicationName,
    };
}
