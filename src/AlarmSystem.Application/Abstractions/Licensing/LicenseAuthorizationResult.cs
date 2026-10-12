namespace AlarmSystem.Application.Abstractions.Licensing;

public sealed record LicenseAuthorizationResult
{
    private LicenseAuthorizationResult(bool isAuthorized, string? failureCode)
    {
        IsAuthorized = isAuthorized;
        FailureCode = failureCode;
    }

    public bool IsAuthorized { get; }

    public string? FailureCode { get; }

    public static LicenseAuthorizationResult Authorized { get; } = new(true, null);

    public static LicenseAuthorizationResult Denied(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (!LicenseAuthorizationFailureCodes.IsKnown(failureCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureCode),
                failureCode,
                "The license authorization failure code is not supported.");
        }

        return new LicenseAuthorizationResult(false, failureCode);
    }
}
