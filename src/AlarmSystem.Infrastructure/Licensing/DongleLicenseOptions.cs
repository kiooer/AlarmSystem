using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing;

public sealed class DongleLicenseOptions
{
    public const string SectionName = "Licensing:Dongle";

    public string Provider { get; init; } = "Unconfigured";

    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public IReadOnlyDictionary<NotificationFeature, string> FeatureMapping { get; init; } =
        new Dictionary<NotificationFeature, string>
        {
            [NotificationFeature.Line] = string.Empty,
            [NotificationFeature.Teams] = string.Empty,
            [NotificationFeature.Email] = string.Empty,
        };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider))
        {
            throw new InvalidOperationException($"{SectionName}:Provider is required.");
        }

        if (CheckTimeout <= TimeSpan.Zero || CheckTimeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                $"{SectionName}:CheckTimeout must be greater than zero and no more than two minutes.");
        }

        foreach (var feature in Enum.GetValues<NotificationFeature>())
        {
            if (!FeatureMapping.ContainsKey(feature))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:FeatureMapping must contain {feature}.");
            }
        }

        if (FeatureMapping.Keys.Any(feature => !Enum.IsDefined(feature)))
        {
            throw new InvalidOperationException(
                $"{SectionName}:FeatureMapping contains an unsupported notification feature.");
        }
    }
}
