namespace AlarmSystem.Application.Abstractions.Licensing;

public interface ILicenseImportService
{
    Task<LicenseValidationResult> PreviewImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        CancellationToken cancellationToken);

    Task<LicenseValidationResult> ImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        CancellationToken cancellationToken);

    Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken);
}
