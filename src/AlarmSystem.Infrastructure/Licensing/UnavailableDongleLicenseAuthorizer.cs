using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing;

public sealed class UnavailableDongleLicenseAuthorizer : INotificationLicenseAuthorizer
{
    private readonly DongleLicenseOptions options;

    public UnavailableDongleLicenseAuthorizer(DongleLicenseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
    }

    public Task<LicenseAuthorizationResult> AuthorizeAsync(
        NotificationFeature feature,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(feature) || !options.FeatureMapping.ContainsKey(feature))
        {
            return Task.FromResult(LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.ValidationFailed));
        }

        return Task.FromResult(LicenseAuthorizationResult.Denied(
            LicenseAuthorizationFailureCodes.DongleUnavailable));
    }
}
