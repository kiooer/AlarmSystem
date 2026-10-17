using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Desktop.Services;

public sealed record LicenseTestOperationResult(
    bool Succeeded,
    bool ServiceConnected,
    LicenseStatus? Status,
    LicenseValidationResult? Validation,
    string? ErrorCode,
    string DiagnosticMessage);

public interface ILicenseTestService
{
    string GetMachineId();

    Task<LicenseTestOperationResult> ReadStatusAsync(CancellationToken cancellationToken);

    Task<LicenseTestOperationResult> PreviewAsync(
        string path,
        CancellationToken cancellationToken);

    Task<LicenseTestOperationResult> ImportAsync(
        string path,
        CancellationToken cancellationToken);

    Task<LicenseTestOperationResult> RefreshAsync(CancellationToken cancellationToken);
}

public interface ILicenseFilePicker
{
    string? SelectLicenseFile();
}

public interface ILicenseImportConfirmationService
{
    bool ConfirmImport(LicenseStatus status);
}

public interface IClipboardService
{
    void SetText(string value);
}
