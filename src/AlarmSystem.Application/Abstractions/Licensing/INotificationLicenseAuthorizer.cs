namespace AlarmSystem.Application.Abstractions.Licensing;

public interface INotificationLicenseAuthorizer
{
    Task<LicenseAuthorizationResult> AuthorizeAsync(
        NotificationFeature feature,
        CancellationToken cancellationToken);
}
