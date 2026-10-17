using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Demo;

namespace AlarmSystem.Worker;

public sealed class LicenseManagementPipeServer(
    DemoLicenseOptions options,
    ILicenseStatusReader statusReader,
    ILicenseImportService importService,
    ILogger<LicenseManagementPipeServer> logger) : BackgroundService
{
    private const int MaximumRequestCharacters = 96 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "License management pipe request failed. ExceptionType={ExceptionType}",
                    exception.GetType().FullName);
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("License management pipes require Windows.");
        }

        using var serviceIdentity = WindowsIdentity.GetCurrent();
        var serviceSid = serviceIdentity.User ??
            throw new InvalidOperationException("The Windows service identity is unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            serviceSid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            options.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorizedClient(pipe))
        {
            await WriteResponseAsync(
                pipe,
                new LicenseManagementResponse(
                    false,
                    true,
                    null,
                    null,
                    "license_management_unauthorized",
                    "The local Windows identity is not authorized for license management."),
                cancellationToken);
            return;
        }

        var requestText = await ReadRequestAsync(pipe, cancellationToken);
        LicenseManagementRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<LicenseManagementRequest>(requestText, JsonOptions);
        }
        catch (JsonException)
        {
            request = null;
        }

        var response = request is null
            ? InvalidRequest("The license management request is invalid.")
            : await DispatchAsync(request, cancellationToken);
        await WriteResponseAsync(pipe, response, cancellationToken);
    }

    private async Task<LicenseManagementResponse> DispatchAsync(
        LicenseManagementRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return request.Operation switch
            {
                LicenseManagementOperations.ReadLicenseStatus => StatusResponse(
                    await statusReader.ReadStatusAsync(cancellationToken)),
                LicenseManagementOperations.RefreshLicenseStatus => StatusResponse(
                    await statusReader.ReadStatusAsync(cancellationToken)),
                LicenseManagementOperations.PreviewLicenseImport => ValidationResponse(
                    await importService.PreviewImportAsync(
                        DecodeLicense(request.LicenseBase64),
                        cancellationToken)),
                LicenseManagementOperations.ImportLicense => ValidationResponse(
                    await importService.ImportAsync(
                        DecodeLicense(request.LicenseBase64),
                        cancellationToken)),
                _ => InvalidRequest("The license management operation is not supported."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FormatException)
        {
            return InvalidRequest("The license content encoding is invalid.");
        }
        catch (Exception)
        {
            return InvalidRequest("The license management operation failed safely.");
        }
    }

    private static ReadOnlyMemory<byte> DecodeLicense(string? licenseBase64)
    {
        if (string.IsNullOrWhiteSpace(licenseBase64))
        {
            throw new FormatException("License content is required.");
        }

        var bytes = Convert.FromBase64String(licenseBase64);
        if (bytes.Length is <= 0 or > DemoLicenseOptions.DefaultMaximumLicenseBytes)
        {
            throw new FormatException("License content size is invalid.");
        }

        return bytes;
    }

    private static bool IsAuthorizedClient(NamedPipeServerStream pipe)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var serviceSid = WindowsIdentity.GetCurrent().User;
        var authorized = false;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var clientIdentity = WindowsIdentity.GetCurrent(true);
                if (clientIdentity is null)
                {
                    authorized = false;
                    return;
                }

                var principal = new WindowsPrincipal(clientIdentity);
                authorized = clientIdentity.IsAuthenticated &&
                    (clientIdentity.User == serviceSid ||
                     principal.IsInRole(WindowsBuiltInRole.Administrator));
            });
        }
        catch
        {
            return false;
        }

        return authorized;
    }

    private static async Task<string> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var newline = Array.IndexOf(buffer, '\n', 0, read);
            var length = newline >= 0 ? newline : read;
            builder.Append(buffer, 0, length);
            if (builder.Length > MaximumRequestCharacters)
            {
                throw new InvalidDataException("The license management request is too large.");
            }

            if (newline >= 0)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        LicenseManagementResponse response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static LicenseManagementResponse StatusResponse(LicenseStatus status) =>
        new(true, true, status, null, null, status.DiagnosticMessage);

    private static LicenseManagementResponse ValidationResponse(LicenseValidationResult validation) =>
        new(
            validation.IsValid,
            true,
            validation.Status,
            validation,
            validation.FailureCode,
            validation.DiagnosticMessage);

    private static LicenseManagementResponse InvalidRequest(string diagnostic) =>
        new(false, true, null, null, "license_management_invalid_request", diagnostic);
}
