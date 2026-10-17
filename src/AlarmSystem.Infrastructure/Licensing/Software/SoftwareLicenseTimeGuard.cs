namespace AlarmSystem.Infrastructure.Licensing.Software;

public enum SoftwareLicenseTimeGuardResult
{
    Valid = 0,
    MissingState = 1,
    InvalidState = 2,
    TimeAnomaly = 3,
}

public sealed class SoftwareLicenseTimeGuard(
    SoftwareLicenseOptions options,
    SoftwareLicenseFileStore fileStore)
{
    public async Task<SoftwareLicenseTimeGuardResult> ValidateAndAdvanceAsync(
        string licenseId,
        string licenseFingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        SoftwareLicenseState? state;
        try
        {
            state = await fileStore.ReadStateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return SoftwareLicenseTimeGuardResult.InvalidState;
        }

        if (state is null)
        {
            return SoftwareLicenseTimeGuardResult.MissingState;
        }

        if (!string.Equals(state.LicenseId, licenseId, StringComparison.Ordinal) ||
            !string.Equals(
                state.LicenseFingerprint,
                licenseFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return SoftwareLicenseTimeGuardResult.InvalidState;
        }

        nowUtc = nowUtc.ToUniversalTime();
        if (nowUtc + options.ClockRollbackTolerance < state.LastSeenUtc)
        {
            return SoftwareLicenseTimeGuardResult.TimeAnomaly;
        }

        if (nowUtc > state.LastSeenUtc)
        {
            try
            {
                await fileStore.WriteStateAsync(
                    state with { LastSeenUtc = nowUtc },
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return SoftwareLicenseTimeGuardResult.InvalidState;
            }
        }

        return SoftwareLicenseTimeGuardResult.Valid;
    }
}
