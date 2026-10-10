namespace AlarmSystem.Infrastructure.Notifications.Teams;

public sealed class TeamsOptions
{
    public const string SectionName = "Notifications:Teams";

    public bool Enabled { get; init; }

    public Uri? Endpoint { get; init; }

    public string DestinationReference { get; init; } = string.Empty;

    public string TemplatePath { get; init; } = "templates/teams-alarm-card.json";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DestinationReference))
        {
            throw new InvalidOperationException(
                $"{SectionName}:DestinationReference is required.");
        }

        if (string.IsNullOrWhiteSpace(TemplatePath))
        {
            throw new InvalidOperationException($"{SectionName}:TemplatePath is required.");
        }

        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Timeout must be greater than zero and no more than two minutes.");
        }

        if (!Enabled)
        {
            return;
        }

        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{SectionName}:Endpoint must be an absolute HTTPS URI when Teams is enabled.");
        }
    }
}
