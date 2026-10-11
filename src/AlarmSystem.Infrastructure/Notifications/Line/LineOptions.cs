namespace AlarmSystem.Infrastructure.Notifications.Line;

public sealed class LineOptions
{
    public const string SectionName = "Notifications:Line";

    public bool Enabled { get; init; }

    public string GroupId { get; init; } = string.Empty;

    public string ChannelAccessToken { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Timeout must be greater than zero and no more than two minutes.");
        }

        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GroupId))
        {
            throw new InvalidOperationException(
                $"{SectionName}:GroupId is required when LINE is enabled.");
        }

        if (string.IsNullOrWhiteSpace(ChannelAccessToken))
        {
            throw new InvalidOperationException(
                $"{SectionName}:ChannelAccessToken is required when LINE is enabled.");
        }
    }
}
