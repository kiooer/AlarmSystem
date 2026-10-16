using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public enum SqlServerConnectionRole
{
    Source = 1,
    AlarmStore = 2,
}

public sealed class SqlServerConnectionOptions
{
    public const string SectionName = "SqlServer";

    public SqlServerConnectionRole Role { get; init; }

    public string Server { get; init; } = string.Empty;

    public string Database { get; init; } = string.Empty;

    public string Authentication { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool? Encrypt { get; init; }

    public bool? TrustServerCertificate { get; init; }

    public int ConnectionTimeoutSeconds { get; init; } = 10;

    public void Validate()
    {
        if (!Enum.IsDefined(Role))
        {
            throw new SqlConfigurationException(
                "SqlServer.Role must be Source or AlarmStore.");
        }

        if (string.IsNullOrWhiteSpace(Server))
        {
            throw new SqlConfigurationException("SqlServer.Server is required.");
        }

        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new SqlConfigurationException("SqlServer.Database is required.");
        }

        if (!string.Equals(Authentication, "SqlPassword", StringComparison.Ordinal))
        {
            throw new SqlConfigurationException(
                "SqlServer.Authentication must be SqlPassword; integrated security is not supported.");
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            throw new SqlConfigurationException("SqlServer.UserName is required.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new SqlConfigurationException("SqlServer.Password is required.");
        }

        if (Encrypt is null)
        {
            throw new SqlConfigurationException("SqlServer.Encrypt must be explicitly true or false.");
        }

        if (TrustServerCertificate is null)
        {
            throw new SqlConfigurationException(
                "SqlServer.TrustServerCertificate must be explicitly true or false.");
        }

        if (ConnectionTimeoutSeconds is < 1 or > 120)
        {
            throw new SqlConfigurationException(
                "SqlServer.ConnectionTimeoutSeconds must be between 1 and 120.");
        }
    }

    public override string ToString() =>
        $"Role={Role};Server={Server};Database={Database};Authentication={Authentication};" +
        $"Encrypt={Encrypt};TrustServerCertificate={TrustServerCertificate};" +
        $"ConnectionTimeoutSeconds={ConnectionTimeoutSeconds};Password=***";
}

public sealed class SqlConfigurationException : Exception
{
    public SqlConfigurationException(string message)
        : base(message)
    {
    }

    public SqlConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class SqlServerConnectionOptionsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<SqlServerConnectionOptions> LoadAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new SqlConfigurationException("A configuration path is required.");
        }

        var fullPath = Path.GetFullPath(configPath);
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var document = await JsonSerializer.DeserializeAsync<SqlConfigurationDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            var options = document?.SqlServer ?? throw new SqlConfigurationException(
                "The configuration must contain a SqlServer object.");
            options.Validate();
            return options;
        }
        catch (SqlConfigurationException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw new SqlConfigurationException(
                $"The SQL configuration file was not found: {fullPath}",
                exception);
        }
        catch (JsonException exception)
        {
            throw new SqlConfigurationException(
                "The SQL configuration file is not valid JSON or contains unsupported fields.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SqlConfigurationException(
                "The SQL configuration file cannot be read with the current Windows permissions.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SqlConfigurationException(
                "The SQL configuration file could not be read.",
                exception);
        }
    }

    private sealed class SqlConfigurationDocument
    {
        public SqlServerConnectionOptions? SqlServer { get; init; }
    }
}
