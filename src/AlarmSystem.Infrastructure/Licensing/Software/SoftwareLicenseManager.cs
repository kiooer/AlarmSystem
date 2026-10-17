using System.Security.Cryptography;
using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Software;

public sealed class SoftwareLicenseManager :
    INotificationLicenseAuthorizer,
    ILicenseStatusReader,
    ILicenseImportService
{
    private readonly SoftwareLicenseOptions options;
    private readonly SoftwareLicenseVerifier verifier;
    private readonly SoftwareLicenseFileStore fileStore;
    private readonly Demo.IMachineFingerprintProvider machineFingerprintProvider;
    private readonly SoftwareLicenseTimeGuard timeGuard;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SoftwareLicenseManager(
        SoftwareLicenseOptions options,
        SoftwareLicenseVerifier verifier,
        SoftwareLicenseFileStore fileStore,
        Demo.IMachineFingerprintProvider machineFingerprintProvider,
        SoftwareLicenseTimeGuard timeGuard,
        TimeProvider timeProvider)
    {
        options.Validate();
        this.options = options;
        this.verifier = verifier;
        this.fileStore = fileStore;
        this.machineFingerprintProvider = machineFingerprintProvider;
        this.timeGuard = timeGuard;
        this.timeProvider = timeProvider;
    }

    public async Task<LicenseAuthorizationResult> AuthorizeAsync(
        NotificationFeature feature,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(feature))
        {
            return LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.ValidationFailed);
        }

        var status = await ReadStatusAsync(cancellationToken);
        if (status.State == LicenseState.Active)
        {
            return status.AuthorizedFeatures.Contains(feature)
                ? LicenseAuthorizationResult.Authorized
                : LicenseAuthorizationResult.Denied(
                    LicenseAuthorizationFailureCodes.FeatureNotLicensed);
        }

        var failureCode = status.FailureCode;
        return LicenseAuthorizationResult.Denied(
            failureCode is not null && LicenseAuthorizationFailureCodes.IsKnown(failureCode)
                ? failureCode
                : LicenseAuthorizationFailureCodes.ValidationFailed);
    }

    public async Task<LicenseStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadStatusCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(cancellationToken);

    public Task<LicenseValidationResult> PreviewImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(VerifyContent(licenseContent));
    }

    public async Task<LicenseValidationResult> ImportAsync(
        ReadOnlyMemory<byte> licenseContent,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var preview = VerifyContent(licenseContent);
            if (!preview.IsValid || string.IsNullOrWhiteSpace(preview.Status.LicenseId))
            {
                return preview;
            }

            var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
            var incomingFingerprint = Fingerprint(licenseContent.Span);
            var initialLastSeenUtc = nowUtc;
            var installedContent = await fileStore.ReadLicenseAsync(cancellationToken);
            var existingState = await ReadExistingStateAsync(cancellationToken);
            if (installedContent is null && existingState is not null)
            {
                return ImportRejected(
                    preview.Status,
                    LicenseState.Invalid,
                    LicenseAuthorizationFailureCodes.StateInvalid,
                    "The protected software license state exists without its license file.");
            }

            if (installedContent is not null)
            {
                if (existingState is null ||
                    !string.Equals(
                        existingState.LicenseFingerprint,
                        Fingerprint(installedContent),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ImportRejected(
                        preview.Status,
                        LicenseState.Invalid,
                        LicenseAuthorizationFailureCodes.StateInvalid,
                        "The existing protected software license state is missing or invalid.");
                }

                if (nowUtc + options.ClockRollbackTolerance < existingState.LastSeenUtc)
                {
                    return ImportRejected(
                        preview.Status,
                        LicenseState.TimeAnomaly,
                        LicenseAuthorizationFailureCodes.StateInvalid,
                        "System time moved backwards; license replacement cannot reset the clock.");
                }

                if (string.Equals(
                        existingState.LicenseId,
                        preview.Status.LicenseId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        existingState.LicenseFingerprint,
                        incomingFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ImportRejected(
                        preview.Status,
                        LicenseState.Invalid,
                        LicenseAuthorizationFailureCodes.StateInvalid,
                        "The same license ID cannot be replaced by different signed content.");
                }

                initialLastSeenUtc = existingState.LastSeenUtc > nowUtc
                    ? existingState.LastSeenUtc
                    : nowUtc;
            }

            await fileStore.ImportAsync(
                licenseContent,
                new SoftwareLicenseState(
                    preview.Status.LicenseId,
                    incomingFingerprint,
                    initialLastSeenUtc),
                cancellationToken);
            var refreshed = await ReadStatusCoreAsync(cancellationToken);
            return refreshed.State is LicenseState.Active or LicenseState.Pending
                ? LicenseValidationResult.Valid(refreshed)
                : LicenseValidationResult.Invalid(
                    refreshed,
                    refreshed.FailureCode ?? LicenseAuthorizationFailureCodes.ValidationFailed,
                    refreshed.DiagnosticMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var status = InvalidStatus(
                timeProvider.GetUtcNow(),
                LicenseState.Invalid,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                "The software license could not be imported. The previous license was preserved when possible.");
            return LicenseValidationResult.Invalid(
                status,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                status.DiagnosticMessage);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LicenseStatus> ReadStatusCoreAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        byte[]? content;
        try
        {
            content = await fileStore.ReadLicenseAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return InvalidStatus(
                nowUtc,
                LicenseState.Invalid,
                LicenseAuthorizationFailureCodes.StateInvalid,
                "The software license file cannot be read.");
        }

        if (content is null)
        {
            return LicenseStatus.Unlicensed(
                nowUtc,
                LicenseAuthorizationFailureCodes.LicenseUnavailable) with
            {
                LicenseType = "Software V2",
                FormatVersion = 2,
            };
        }

        var validation = VerifyContent(content);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.Status.LicenseId))
        {
            return validation.Status;
        }

        var timeResult = await timeGuard.ValidateAndAdvanceAsync(
            validation.Status.LicenseId,
            Fingerprint(content),
            nowUtc,
            cancellationToken);
        return timeResult switch
        {
            SoftwareLicenseTimeGuardResult.Valid => validation.Status,
            SoftwareLicenseTimeGuardResult.TimeAnomaly => validation.Status with
            {
                State = LicenseState.TimeAnomaly,
                FailureCode = LicenseAuthorizationFailureCodes.StateInvalid,
                DiagnosticMessage = "System time moved backwards beyond the allowed tolerance.",
            },
            SoftwareLicenseTimeGuardResult.MissingState => validation.Status with
            {
                State = LicenseState.Invalid,
                FailureCode = LicenseAuthorizationFailureCodes.StateInvalid,
                DiagnosticMessage = "The protected software license state is missing.",
            },
            _ => validation.Status with
            {
                State = LicenseState.Invalid,
                FailureCode = LicenseAuthorizationFailureCodes.StateInvalid,
                DiagnosticMessage = "The protected software license state is invalid.",
            },
        };
    }

    private LicenseValidationResult VerifyContent(ReadOnlyMemory<byte> content)
    {
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        if (content.Length <= 0 || content.Length > options.MaximumLicenseBytes)
        {
            var status = InvalidStatus(
                nowUtc,
                LicenseState.Invalid,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                "The software license file size is invalid.");
            return LicenseValidationResult.Invalid(
                status,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                status.DiagnosticMessage);
        }

        try
        {
            return verifier.Verify(content, machineFingerprintProvider.GetMachineId(), nowUtc);
        }
        catch
        {
            var status = InvalidStatus(
                nowUtc,
                LicenseState.Invalid,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                "Machine identity is unavailable.");
            return LicenseValidationResult.Invalid(
                status,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                status.DiagnosticMessage);
        }
    }

    private async Task<SoftwareLicenseState?> ReadExistingStateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileStore.ReadStateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string Fingerprint(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private static LicenseStatus InvalidStatus(
        DateTimeOffset nowUtc,
        LicenseState state,
        string failureCode,
        string diagnostic) =>
        new(
            LicenseSource.Software,
            state,
            null,
            "Software V2",
            null,
            null,
            null,
            null,
            null,
            [],
            nowUtc,
            failureCode,
            diagnostic,
            FormatVersion: 2,
            SignatureValid: false);

    private static LicenseValidationResult ImportRejected(
        LicenseStatus previewStatus,
        LicenseState state,
        string failureCode,
        string diagnostic)
    {
        var status = previewStatus with
        {
            State = state,
            FailureCode = failureCode,
            DiagnosticMessage = diagnostic,
        };
        return LicenseValidationResult.Invalid(status, failureCode, diagnostic);
    }
}
