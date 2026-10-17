using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Application.Notifications;

public sealed class CompositeLicenseStatusReader(
    INotificationLicenseAuthorizer dongleAuthorizer,
    ILicenseStatusReader softwareStatusReader,
    ILicenseStatusReader demoStatusReader,
    TimeProvider timeProvider) : ILicenseStatusReader
{
    public async Task<LicenseStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        var dongleFeatures = new HashSet<NotificationFeature>();
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

        var softwareStatus = await ReadSafelyAsync(
            softwareStatusReader,
            LicenseSource.Software,
            "Software V2",
            cancellationToken);
        var demoStatus = await ReadSafelyAsync(
            demoStatusReader,
            LicenseSource.Demo,
            "Demo V1",
            cancellationToken);
        var sourceByFeature = new Dictionary<NotificationFeature, LicenseSource>();
        foreach (var feature in Enum.GetValues<NotificationFeature>())
        {
            if (dongleFeatures.Contains(feature))
            {
                sourceByFeature[feature] = LicenseSource.Dongle;
            }
            else if (IsAuthorized(softwareStatus, feature))
            {
                sourceByFeature[feature] = LicenseSource.Software;
            }
            else if (IsAuthorized(demoStatus, feature))
            {
                sourceByFeature[feature] = LicenseSource.Demo;
            }
        }

        if (sourceByFeature.Count == 0)
        {
            return SelectMostRelevantFailure(softwareStatus, demoStatus);
        }

        var activeSources = sourceByFeature.Values.Distinct().ToArray();
        if (activeSources.Length == 1)
        {
            return activeSources[0] switch
            {
                LicenseSource.Dongle => CreateDongleStatus(sourceByFeature.Keys.ToArray()),
                LicenseSource.Software => softwareStatus with
                {
                    AuthorizedFeatures = sourceByFeature.Keys.Order().ToArray(),
                    FeatureSources = sourceByFeature,
                },
                _ => demoStatus with
                {
                    AuthorizedFeatures = sourceByFeature.Keys.Order().ToArray(),
                    FeatureSources = sourceByFeature,
                },
            };
        }

        var sourceNames = activeSources
            .OrderBy(SourcePriority)
            .Select(SourceName)
            .ToArray();
        var licenseIds = new[]
        {
            activeSources.Contains(LicenseSource.Software) ? softwareStatus.LicenseId : null,
            activeSources.Contains(LicenseSource.Demo) ? demoStatus.LicenseId : null,
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return new LicenseStatus(
            LicenseSource.Composite,
            LicenseState.Active,
            licenseIds.Length == 0 ? null : string.Join(" + ", licenseIds),
            string.Join(" + ", sourceNames),
            null,
            null,
            null,
            null,
            null,
            sourceByFeature.Keys.Order().ToArray(),
            timeProvider.GetUtcNow(),
            null,
            "Multiple independent license sources are active for different features.",
            sourceByFeature,
            SignatureValid: ActiveSoftwareSignaturesValid(activeSources, softwareStatus, demoStatus));
    }

    private async Task<LicenseStatus> ReadSafelyAsync(
        ILicenseStatusReader reader,
        LicenseSource source,
        string licenseType,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LicenseStatus(
                source,
                LicenseState.Invalid,
                null,
                licenseType,
                null,
                null,
                null,
                null,
                null,
                [],
                timeProvider.GetUtcNow(),
                LicenseAuthorizationFailureCodes.ValidationFailed,
                $"The {licenseType} status could not be read.");
        }
    }

    private static bool IsAuthorized(LicenseStatus status, NotificationFeature feature) =>
        status.State == LicenseState.Active && status.AuthorizedFeatures.Contains(feature);

    private static LicenseStatus SelectMostRelevantFailure(
        LicenseStatus softwareStatus,
        LicenseStatus demoStatus) =>
        softwareStatus.State != LicenseState.Unlicensed ? softwareStatus : demoStatus;

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
        "Dongle security validation failed; software and demo fallback were not used.");

    private static int SourcePriority(LicenseSource source) => source switch
    {
        LicenseSource.Dongle => 0,
        LicenseSource.Software => 1,
        LicenseSource.Demo => 2,
        _ => 3,
    };

    private static string SourceName(LicenseSource source) => source switch
    {
        LicenseSource.Dongle => "Dongle",
        LicenseSource.Software => "Software V2",
        LicenseSource.Demo => "Demo V1",
        _ => "Unknown",
    };

    private static bool? ActiveSoftwareSignaturesValid(
        IReadOnlyCollection<LicenseSource> sources,
        LicenseStatus softwareStatus,
        LicenseStatus demoStatus)
    {
        var values = new List<bool>();
        if (sources.Contains(LicenseSource.Software) && softwareStatus.SignatureValid.HasValue)
        {
            values.Add(softwareStatus.SignatureValid.Value);
        }

        if (sources.Contains(LicenseSource.Demo) && demoStatus.SignatureValid.HasValue)
        {
            values.Add(demoStatus.SignatureValid.Value);
        }

        return values.Count == 0 ? null : values.All(value => value);
    }
}
