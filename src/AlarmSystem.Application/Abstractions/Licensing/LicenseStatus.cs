namespace AlarmSystem.Application.Abstractions.Licensing;

public enum LicenseSource
{
    None = 0,
    Dongle = 1,
    Demo = 2,
    Composite = 3,
}

public enum LicenseState
{
    Unlicensed = 0,
    Active = 1,
    Expired = 2,
    Invalid = 3,
    TimeAnomaly = 4,
}

public sealed record LicenseStatus(
    LicenseSource Source,
    LicenseState State,
    string? LicenseId,
    string LicenseType,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc,
    int? RemainingDays,
    bool? MachineIdMatches,
    IReadOnlyList<NotificationFeature> AuthorizedFeatures,
    DateTimeOffset ValidatedAtUtc,
    string? FailureCode,
    string DiagnosticMessage,
    IReadOnlyDictionary<NotificationFeature, LicenseSource>? FeatureSources = null)
{
    public static LicenseStatus Unlicensed(DateTimeOffset validatedAtUtc, string failureCode) =>
        new(
            LicenseSource.None,
            LicenseState.Unlicensed,
            null,
            "Unlicensed",
            null,
            null,
            null,
            null,
            null,
            [],
            validatedAtUtc,
            failureCode,
            "No active notification license is available.");
}
