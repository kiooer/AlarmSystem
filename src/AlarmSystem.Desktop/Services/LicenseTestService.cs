using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing;
using AlarmSystem.Infrastructure.Licensing.Demo;
using AlarmSystem.Infrastructure.Licensing.Software;

namespace AlarmSystem.Desktop.Services;

public sealed class LicenseTestService(
    DemoLicenseOptions options,
    IMachineFingerprintProvider fingerprintProvider,
    DemoLicenseVerifier demoVerifier,
    SoftwareLicenseVerifier softwareVerifier,
    TimeProvider timeProvider) : ILicenseTestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GetMachineId() => fingerprintProvider.GetMachineId();

    public Task<LicenseTestOperationResult> ReadStatusAsync(CancellationToken cancellationToken) =>
        SendAsync(
            new LicenseManagementRequest(LicenseManagementOperations.ReadLicenseStatus, null),
            cancellationToken);

    public Task<LicenseTestOperationResult> RefreshAsync(CancellationToken cancellationToken) =>
        SendAsync(
            new LicenseManagementRequest(LicenseManagementOperations.RefreshLicenseStatus, null),
            cancellationToken);

    public async Task<LicenseTestOperationResult> PreviewAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var content = await ReadLicenseFileAsync(path, cancellationToken);
        var response = await SendAsync(
            new LicenseManagementRequest(
                LicenseManagementOperations.PreviewLicenseImport,
                Convert.ToBase64String(content)),
            cancellationToken);
        if (response.ServiceConnected)
        {
            return response;
        }

        var machineId = fingerprintProvider.GetMachineId();
        var nowUtc = timeProvider.GetUtcNow();
        var validation = LicenseFormatDetector.TryDetect(content) switch
        {
            1 => demoVerifier.Verify(content, machineId, nowUtc),
            2 => softwareVerifier.Verify(content, machineId, nowUtc),
            _ => Unsupported(nowUtc),
        };
        return new LicenseTestOperationResult(
            validation.IsValid,
            false,
            validation.Status,
            validation,
            validation.FailureCode,
            $"Worker service is not connected; this is a read-only preview and has not been activated. {validation.DiagnosticMessage}");
    }

    public async Task<LicenseTestOperationResult> ImportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var content = await ReadLicenseFileAsync(path, cancellationToken);
        var response = await SendAsync(
            new LicenseManagementRequest(
                LicenseManagementOperations.ImportLicense,
                Convert.ToBase64String(content)),
            cancellationToken);
        return response.ServiceConnected
            ? response
            : response with
            {
                Succeeded = false,
                DiagnosticMessage = "Worker service is not connected; the license was not activated.",
            };
    }

    private async Task<byte[]> ReadLicenseFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".lic", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A .lic file is required.");
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length <= 0 || info.Length > options.MaximumLicenseBytes)
        {
            throw new InvalidDataException("The license file is missing, empty, or larger than 64 KB.");
        }

        var content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (content.Length <= 0 || content.Length > options.MaximumLicenseBytes)
        {
            throw new InvalidDataException("The license file size changed while it was being read.");
        }

        return content;
    }

    private async Task<LicenseTestOperationResult> SendAsync(
        LicenseManagementRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                options.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Impersonation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(timeout.Token);

            var requestJson = JsonSerializer.Serialize(request, JsonOptions) + "\n";
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(requestJson), cancellationToken);
            await pipe.FlushAsync(cancellationToken);
            using var reader = new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var responseJson = await reader.ReadLineAsync(cancellationToken);
            var response = responseJson is null
                ? null
                : JsonSerializer.Deserialize<LicenseManagementResponse>(responseJson, JsonOptions);
            return response is null
                ? Unavailable("The Worker returned an invalid license management response.")
                : new LicenseTestOperationResult(
                    response.Succeeded,
                    response.ServiceConnected,
                    response.Status,
                    response.Validation,
                    response.ErrorCode,
                    response.DiagnosticMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or OperationCanceledException or
            UnauthorizedAccessException or JsonException)
        {
            return Unavailable("Worker service is not connected or access was denied.");
        }
    }

    private static LicenseTestOperationResult Unavailable(string diagnostic) =>
        new(false, false, null, null, "license_service_unavailable", diagnostic);

    private static LicenseValidationResult Unsupported(DateTimeOffset nowUtc)
    {
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
