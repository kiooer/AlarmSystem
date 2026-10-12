namespace AlarmSystem.Application.Abstractions.Licensing;

public static class LicenseAuthorizationFailureCodes
{
    public const string FeatureNotLicensed = "license_feature_not_licensed";
    public const string Expired = "license_expired";
    public const string DongleUnavailable = "license_dongle_unavailable";
    public const string ValidationFailed = "license_validation_failed";

    private static readonly HashSet<string> KnownCodes =
    [
        FeatureNotLicensed,
        Expired,
        DongleUnavailable,
        ValidationFailed,
    ];

    public static bool IsKnown(string failureCode) => KnownCodes.Contains(failureCode);
}
