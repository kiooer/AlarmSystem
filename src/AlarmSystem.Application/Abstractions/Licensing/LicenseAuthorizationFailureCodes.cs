namespace AlarmSystem.Application.Abstractions.Licensing;

public static class LicenseAuthorizationFailureCodes
{
    public const string FeatureNotLicensed = "license_feature_not_licensed";
    public const string Expired = "license_expired";
    public const string DongleUnavailable = "license_dongle_unavailable";
    public const string ValidationFailed = "license_validation_failed";
    public const string LicenseUnavailable = "license_unavailable";
    public const string MachineMismatch = "license_machine_mismatch";
    public const string SignatureInvalid = "license_signature_invalid";
    public const string StateInvalid = "license_state_invalid";
    public const string NotYetActive = "license_not_yet_active";

    private static readonly HashSet<string> KnownCodes =
    [
        FeatureNotLicensed,
        Expired,
        DongleUnavailable,
        ValidationFailed,
        LicenseUnavailable,
        MachineMismatch,
        SignatureInvalid,
        StateInvalid,
        NotYetActive,
    ];

    public static bool IsKnown(string failureCode) => KnownCodes.Contains(failureCode);
}
