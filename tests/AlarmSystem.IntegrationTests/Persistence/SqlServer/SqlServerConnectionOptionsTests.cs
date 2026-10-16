using System.Text.Json;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;

namespace AlarmSystem.IntegrationTests.Persistence.SqlServer;

public sealed class SqlServerConnectionOptionsTests
{
    [Theory]
    [InlineData(@"IFIX-PC\SQLEXPRESS")]
    [InlineData("sql.example.invalid")]
    [InlineData("tcp:192.0.2.10,14330")]
    public void ConnectionFactory_PreservesConfiguredRemoteServer(string server)
    {
        var options = ValidOptions(server: server);

        var builder = new SqlServerConnectionFactory(options).CreateConnectionStringBuilder();

        Assert.Equal(server, builder.DataSource);
        Assert.Equal("IFIX-ALARM", builder.InitialCatalog);
        Assert.Equal(SqlAuthenticationMethod.SqlPassword, builder.Authentication);
        Assert.False(builder.IntegratedSecurity);
        Assert.True(builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
        Assert.False(builder.PersistSecurityInfo);
    }

    [Fact]
    public void ConnectionFactory_PreservesPasswordSpecialCharacters()
    {
        const string password = "p;a=s}s'w\"ord";
        var options = ValidOptions(password: password);

        var builder = new SqlServerConnectionFactory(options).CreateConnectionStringBuilder();

        Assert.Equal(password, builder.Password);
        Assert.DoesNotContain(password, options.ToString(), StringComparison.Ordinal);
        Assert.Contains("Password=***", options.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Validate_RejectsInvalidConfiguration(SqlServerConnectionOptions options)
    {
        Assert.Throws<SqlConfigurationException>(options.Validate);
    }

    [Fact]
    public async Task Loader_ReadsPrivateJsonWithoutLeakingPasswordInDiagnostics()
    {
        const string password = "private-value;with=specials";
        var path = Path.Combine(Path.GetTempPath(), $"alarm-sql-{Guid.NewGuid():N}.json");
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                SqlServer = new
                {
                    Role = "Source",
                    Server = @"REMOTE\INSTANCE",
                    Database = "IFIX-ALARM",
                    Authentication = "SqlPassword",
                    UserName = "sql_user",
                    Password = password,
                    Encrypt = true,
                    TrustServerCertificate = false,
                    ConnectionTimeoutSeconds = 10,
                },
            });
            await File.WriteAllTextAsync(path, json);

            var options = await SqlServerConnectionOptionsLoader.LoadAsync(path);

            Assert.Equal(password, options.Password);
            Assert.DoesNotContain(password, options.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Loader_RejectsUnknownJsonFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"alarm-sql-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "SqlServer": {
                    "Role": "Source",
                    "Server": "remote",
                    "Database": "IFIX-ALARM",
                    "Authentication": "SqlPassword",
                    "UserName": "user",
                    "Password": "secret",
                    "Encrypt": true,
                    "TrustServerCertificate": false,
                    "ConnectionTimeoutSeconds": 10,
                    "IntegratedSecurity": true
                  }
                }
                """);

            var exception = await Assert.ThrowsAsync<SqlConfigurationException>(
                () => SqlServerConnectionOptionsLoader.LoadAsync(path));

            Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static TheoryData<SqlServerConnectionOptions> InvalidOptions() => new()
    {
        ValidOptions(role: (SqlServerConnectionRole)0),
        ValidOptions(server: " "),
        ValidOptions(database: ""),
        ValidOptions(authentication: "Integrated"),
        ValidOptions(userName: ""),
        ValidOptions(password: ""),
        ValidOptions(encrypt: null),
        ValidOptions(trustServerCertificate: null),
        ValidOptions(timeout: 0),
        ValidOptions(timeout: 121),
    };

    internal static SqlServerConnectionOptions ValidOptions(
        SqlServerConnectionRole role = SqlServerConnectionRole.Source,
        string server = @"IFIX-PC\SQLEXPRESS",
        string database = "IFIX-ALARM",
        string authentication = "SqlPassword",
        string userName = "sql_user",
        string password = "not-a-real-password",
        bool? encrypt = true,
        bool? trustServerCertificate = false,
        int timeout = 10) => new()
        {
            Role = role,
            Server = server,
            Database = database,
            Authentication = authentication,
            UserName = userName,
            Password = password,
            Encrypt = encrypt,
            TrustServerCertificate = trustServerCertificate,
            ConnectionTimeoutSeconds = timeout,
        };
}
