namespace AlarmSystem.Infrastructure.Licensing.Demo;

public enum LicenseTimeGuardResult
{
    Valid = 0,
    MissingState = 1,
    InvalidState = 2,
    TimeAnomaly = 3,
}

public sealed class LicenseTimeGuard(
    DemoLicenseOptions options,
    DemoLicenseFileStore fileStore)
{
    public async Task<LicenseTimeGuardResult> ValidateAndAdvanceAsync(
        string licenseId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        DemoLicenseState? state;
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
            return LicenseTimeGuardResult.InvalidState;
        }

        if (state is null)
        {
            return LicenseTimeGuardResult.MissingState;
        }

        if (!string.Equals(state.LicenseId, licenseId, StringComparison.Ordinal))
        {
            return LicenseTimeGuardResult.InvalidState;
        }

        nowUtc = nowUtc.ToUniversalTime();
        if (nowUtc + options.ClockRollbackTolerance < state.LastSeenUtc)
        {
            return LicenseTimeGuardResult.TimeAnomaly;
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
                return LicenseTimeGuardResult.InvalidState;
            }
        }

        return LicenseTimeGuardResult.Valid;
    }
}
