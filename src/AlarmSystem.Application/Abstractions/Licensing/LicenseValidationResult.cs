namespace AlarmSystem.Application.Abstractions.Licensing;

public sealed record LicenseValidationResult(
    bool IsValid,
    LicenseStatus Status,
    string? FailureCode,
    string DiagnosticMessage)
{
    public static LicenseValidationResult Valid(LicenseStatus status) =>
        new(true, status, null, "License validation succeeded.");

    public static LicenseValidationResult Invalid(
        LicenseStatus status,
        string failureCode,
        string diagnosticMessage) =>
        new(false, status, failureCode, diagnosticMessage);
}
