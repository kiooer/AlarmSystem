using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Application.Notifications;

public sealed class CompositeLicenseStatusReader(
    INotificationLicenseAuthorizer dongleAuthorizer,
    ILicenseStatusReader demoStatusReader,
    TimeProvider timeProvider) : ILicenseStatusReader
{
    public async Task<LicenseStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        var dongleFeatures = new List<NotificationFeature>();
        foreach (var feature in Enum.GetValues<NotificationFeature>())
        {
            LicenseAuthorizationResult result;
            try
            {
                result = await dongleAuthorizer.AuthorizeAsync(feature, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return InvalidDongleStatus();
            }

            if (result.IsAuthorized)
            {
                dongleFeatures.Add(feature);
            }
            else if (result.FailureCode == LicenseAuthorizationFailureCodes.ValidationFailed)
            {
                return InvalidDongleStatus();
            }
        }

        if (dongleFeatures.Count == Enum.GetValues<NotificationFeature>().Length)
        {
            return CreateDongleStatus(dongleFeatures);
        }

        var demoStatus = await demoStatusReader.ReadStatusAsync(cancellationToken);
        if (dongleFeatures.Count == 0)
        {
            return demoStatus.State == LicenseState.Active
                ? demoStatus with
                {
                    FeatureSources = demoStatus.AuthorizedFeatures.ToDictionary(
                        feature => feature,
                        _ => LicenseSource.Demo),
                }
                : demoStatus;
        }

        var demoFeatures = demoStatus.State == LicenseState.Active
            ? demoStatus.AuthorizedFeatures.Except(dongleFeatures).ToArray()
            : [];
        if (demoFeatures.Length == 0)
        {
            return CreateDongleStatus(dongleFeatures);
        }

        var sources = dongleFeatures.ToDictionary(feature => feature, _ => LicenseSource.Dongle);
        foreach (var feature in demoFeatures)
        {
            sources[feature] = LicenseSource.Demo;
        }

        return demoStatus with
        {
            Source = LicenseSource.Composite,
            LicenseType = "Dongle + Demo",
            AuthorizedFeatures = dongleFeatures.Concat(demoFeatures).Order().ToArray(),
            DiagnosticMessage = "Dongle and demo licenses are active for different features.",
            FeatureSources = sources,
        };
    }

    private LicenseStatus CreateDongleStatus(IReadOnlyList<NotificationFeature> features) => new(
        LicenseSource.Dongle,
        LicenseState.Active,
        null,
        "Dongle",
        null,
        null,
        null,
        null,
        null,
        features,
        timeProvider.GetUtcNow(),
        null,
        "Dongle license is active.",
        features.ToDictionary(feature => feature, _ => LicenseSource.Dongle));

    private LicenseStatus InvalidDongleStatus() => new(
        LicenseSource.Dongle,
        LicenseState.Invalid,
        null,
        "Dongle",
        null,
        null,
        null,
        null,
        null,
        [],
        timeProvider.GetUtcNow(),
        LicenseAuthorizationFailureCodes.ValidationFailed,
        "Dongle security validation failed; demo fallback was not used.");
}
