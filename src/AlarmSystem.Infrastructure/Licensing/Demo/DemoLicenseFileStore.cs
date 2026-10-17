using System.Text.Json;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed class DemoLicenseFileStore(
    DemoLicenseOptions options,
    ILicenseStateProtector stateProtector,
    ILicenseDirectorySecurity directorySecurity)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<byte[]?> ReadLicenseAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.LicensePath))
        {
            return null;
        }

        var info = new FileInfo(options.LicensePath);
        if (info.Length <= 0 || info.Length > options.MaximumLicenseBytes)
        {
            throw new InvalidDataException("The demo license file size is invalid.");
        }

        var content = await File.ReadAllBytesAsync(options.LicensePath, cancellationToken);
        if (content.Length <= 0 || content.Length > options.MaximumLicenseBytes)
        {
            throw new InvalidDataException("The demo license file size is invalid.");
        }

        return content;
    }

    public async Task<DemoLicenseState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.StatePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(options.StatePath, cancellationToken);
        if (protectedBytes.Length == 0 || protectedBytes.Length > 64 * 1024)
        {
            throw new InvalidDataException("The demo license state file size is invalid.");
        }

        var plaintext = stateProtector.Unprotect(protectedBytes);
        try
        {
            return JsonSerializer.Deserialize<DemoLicenseState>(plaintext, JsonOptions) ??
                throw new InvalidDataException("The demo license state is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The demo license state is invalid.", exception);
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    public async Task WriteStateAsync(
        DemoLicenseState state,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            directorySecurity.EnsureSecureDirectory(Path.GetDirectoryName(options.StatePath)!);
            await WriteProtectedStateAtomicAsync(state, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        DemoLicenseState initialState,
        CancellationToken cancellationToken)
    {
        if (licenseContent.Length <= 0 || licenseContent.Length > options.MaximumLicenseBytes)
        {
            throw new InvalidDataException("The demo license file size is invalid.");
        }

        await gate.WaitAsync(cancellationToken);
        byte[]? oldLicense = null;
        byte[]? oldState = null;
        try
        {
            var licenseDirectory = Path.GetDirectoryName(options.LicensePath)!;
            var stateDirectory = Path.GetDirectoryName(options.StatePath)!;
            directorySecurity.EnsureSecureDirectory(licenseDirectory);
            directorySecurity.EnsureSecureDirectory(stateDirectory);
            oldLicense = File.Exists(options.LicensePath)
                ? await File.ReadAllBytesAsync(options.LicensePath, cancellationToken)
                : null;
            oldState = File.Exists(options.StatePath)
                ? await File.ReadAllBytesAsync(options.StatePath, cancellationToken)
                : null;

            await WriteProtectedStateAtomicAsync(initialState, cancellationToken);
            await WriteAtomicAsync(options.LicensePath, licenseContent, cancellationToken);
        }
        catch
        {
            await RestoreAsync(options.LicensePath, oldLicense);
            await RestoreAsync(options.StatePath, oldState);
            throw;
        }
        finally
        {
            if (oldState is not null)
            {
                Array.Clear(oldState);
            }

            gate.Release();
        }
    }

    private async Task WriteProtectedStateAtomicAsync(
        DemoLicenseState state,
        CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        try
        {
            var protectedBytes = stateProtector.Protect(plaintext);
            try
            {
                await WriteAtomicAsync(options.StatePath, protectedBytes, cancellationToken);
            }
            finally
            {
                Array.Clear(protectedBytes);
            }
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    private static async Task WriteAtomicAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task RestoreAsync(string path, byte[]? original)
    {
        try
        {
            if (original is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            else
            {
                await WriteAtomicAsync(path, original, CancellationToken.None);
            }
        }
        catch
        {
            // Preserve the original import failure. Operators must inspect file permissions.
        }
    }

}
