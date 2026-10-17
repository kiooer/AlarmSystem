using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Demo;
using AlarmSystem.Infrastructure.Licensing.Software;

namespace AlarmSystem.Infrastructure.Licensing;

public sealed class LicenseImportCoordinator(
    DemoLicenseManager demoManager,
    SoftwareLicenseManager softwareManager,
    ILicenseStatusReader compositeStatusReader,
    TimeProvider timeProvider) : ILicenseImportService
{
    public Task<LicenseValidationResult> PreviewImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        CancellationToken cancellationToken) =>
        LicenseFormatDetector.TryDetect(licenseContent) switch
        {
            1 => demoManager.PreviewImportAsync(licenseContent, cancellationToken),
            2 => softwareManager.PreviewImportAsync(licenseContent, cancellationToken),
            _ => Task.FromResult(Unsupported()),
        };

    public Task<LicenseValidationResult> ImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        CancellationToken cancellationToken) =>
        LicenseFormatDetector.TryDetect(licenseContent) switch
        {
            1 => demoManager.ImportAsync(licenseContent, cancellationToken),
            2 => softwareManager.ImportAsync(licenseContent, cancellationToken),
            _ => Task.FromResult(Unsupported()),
        };

    public Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken) =>
        compositeStatusReader.ReadStatusAsync(cancellationToken);

    private LicenseValidationResult Unsupported()
    {
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var status = new LicenseStatus(
            LicenseSource.None,
            LicenseState.Invalid,
            null,
            "Unknown",
            null,
            null,
            null,
            null,
            null,
            [],
            nowUtc,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            "The license format version is missing or unsupported.",
            SignatureValid: false);
        return LicenseValidationResult.Invalid(
            status,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            status.DiagnosticMessage);
    }
}
