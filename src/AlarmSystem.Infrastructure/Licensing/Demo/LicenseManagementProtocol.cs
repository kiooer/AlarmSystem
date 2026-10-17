using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public static class LicenseManagementOperations
{
    public const string ReadLicenseStatus = nameof(ReadLicenseStatus);
    public const string PreviewLicenseImport = nameof(PreviewLicenseImport);
    public const string ImportLicense = nameof(ImportLicense);
    public const string RefreshLicenseStatus = nameof(RefreshLicenseStatus);
}

public sealed record LicenseManagementRequest(string Operation, string? LicenseBase64);

public sealed record LicenseManagementResponse(
    bool Succeeded,
    bool ServiceConnected,
    LicenseStatus? Status,
    LicenseValidationResult? Validation,
    string? ErrorCode,
    string DiagnosticMessage);
