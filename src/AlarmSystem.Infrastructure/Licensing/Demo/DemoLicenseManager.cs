using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed class DemoLicenseManager :
    INotificationLicenseAuthorizer,
    ILicenseStatusReader,
    ILicenseImportService
{
    private readonly DemoLicenseOptions options;
    private readonly DemoLicenseVerifier verifier;
    private readonly DemoLicenseFileStore fileStore;
    private readonly IMachineFingerprintProvider machineFingerprintProvider;
    private readonly LicenseTimeGuard timeGuard;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);

    public DemoLicenseManager(
        DemoLicenseOptions options,
        DemoLicenseVerifier verifier,
        DemoLicenseFileStore fileStore,
        IMachineFingerprintProvider machineFingerprintProvider,
        LicenseTimeGuard timeGuard,
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

        return LicenseAuthorizationResult.Denied(status.FailureCode switch
        {
            LicenseAuthorizationFailureCodes.Expired => LicenseAuthorizationFailureCodes.Expired,
            LicenseAuthorizationFailureCodes.DongleUnavailable =>
                LicenseAuthorizationFailureCodes.DongleUnavailable,
            _ => LicenseAuthorizationFailureCodes.ValidationFailed,
        });
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

            var nowUtc = timeProvider.GetUtcNow();
            var initialLastSeenUtc = nowUtc;
            var installedContent = await fileStore.ReadLicenseAsync(cancellationToken);
            if (installedContent is not null)
            {
                var installed = VerifyContent(installedContent);
                if (string.Equals(
                    installed.Status.LicenseId,
                    preview.Status.LicenseId,
                    StringComparison.Ordinal))
                {
                    DemoLicenseState? existingState;
                    try
                    {
                        existingState = await fileStore.ReadStateAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        existingState = null;
                    }

                    if (existingState is null ||
                        !string.Equals(
                            existingState.LicenseId,
                            preview.Status.LicenseId,
                            StringComparison.Ordinal))
                    {
                        return ImportRejected(
                            preview.Status,
                            LicenseState.Invalid,
                            "The existing protected license state is missing or invalid.");
                    }

                    if (nowUtc + options.ClockRollbackTolerance < existingState.LastSeenUtc)
                    {
                        return ImportRejected(
                            preview.Status,
                            LicenseState.TimeAnomaly,
                            "System time moved backwards; reimport cannot reset the trial clock.");
                    }

                    initialLastSeenUtc = existingState.LastSeenUtc > nowUtc
                        ? existingState.LastSeenUtc
                        : nowUtc;
                }
            }

            await fileStore.ImportAsync(
                licenseContent,
                new DemoLicenseState(preview.Status.LicenseId, initialLastSeenUtc),
                cancellationToken);
            var refreshed = await ReadStatusCoreAsync(cancellationToken);
            return refreshed.State == LicenseState.Active
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
        catch (Exception)
        {
            var nowUtc = timeProvider.GetUtcNow();
            var status = new LicenseStatus(
                LicenseSource.Demo,
                LicenseState.Invalid,
                null,
                "Demo",
                null,
                null,
                null,
                null,
                null,
                [],
                nowUtc,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                "The license could not be imported. The previous license was preserved when possible.");
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
        var nowUtc = timeProvider.GetUtcNow();
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
            return InvalidStatus(nowUtc, LicenseState.Invalid, "The demo license file cannot be read.");
        }

        if (content is null)
        {
            return LicenseStatus.Unlicensed(
                nowUtc,
                LicenseAuthorizationFailureCodes.DongleUnavailable);
        }

        var validation = VerifyContent(content);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.Status.LicenseId))
        {
            return validation.Status;
        }

        var timeResult = await timeGuard.ValidateAndAdvanceAsync(
            validation.Status.LicenseId,
            nowUtc,
            cancellationToken);
        return timeResult switch
        {
            LicenseTimeGuardResult.Valid => validation.Status,
            LicenseTimeGuardResult.TimeAnomaly => validation.Status with
            {
                State = LicenseState.TimeAnomaly,
                FailureCode = LicenseAuthorizationFailureCodes.ValidationFailed,
                DiagnosticMessage = "System time moved backwards beyond the allowed tolerance.",
            },
            LicenseTimeGuardResult.MissingState => validation.Status with
            {
                State = LicenseState.Invalid,
                FailureCode = LicenseAuthorizationFailureCodes.ValidationFailed,
                DiagnosticMessage = "The protected license state is missing.",
            },
            _ => validation.Status with
            {
                State = LicenseState.Invalid,
                FailureCode = LicenseAuthorizationFailureCodes.ValidationFailed,
                DiagnosticMessage = "The protected license state is invalid.",
            },
        };
    }

    private LicenseValidationResult VerifyContent(ReadOnlyMemory<byte> content)
    {
        var nowUtc = timeProvider.GetUtcNow();
        if (content.Length <= 0 || content.Length > options.MaximumLicenseBytes)
        {
            var status = InvalidStatus(nowUtc, LicenseState.Invalid, "The demo license file size is invalid.");
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
            var status = InvalidStatus(nowUtc, LicenseState.Invalid, "Machine identity is unavailable.");
            return LicenseValidationResult.Invalid(
                status,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                status.DiagnosticMessage);
        }
    }

    private static LicenseStatus InvalidStatus(
        DateTimeOffset nowUtc,
        LicenseState state,
        string diagnostic) =>
        new(
            LicenseSource.Demo,
            state,
            null,
            "Demo",
            null,
            null,
            null,
            null,
            null,
            [],
            nowUtc,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);

    private static LicenseValidationResult ImportRejected(
        LicenseStatus previewStatus,
        LicenseState state,
        string diagnostic)
    {
        var status = previewStatus with
        {
            State = state,
            FailureCode = LicenseAuthorizationFailureCodes.ValidationFailed,
            DiagnosticMessage = diagnostic,
        };
        return LicenseValidationResult.Invalid(
            status,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);
    }
}
