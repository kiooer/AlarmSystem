using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Application.Notifications;

public sealed class CompositeLicenseAuthorizer(
    INotificationLicenseAuthorizer dongleAuthorizer,
    INotificationLicenseAuthorizer softwareAuthorizer,
    INotificationLicenseAuthorizer demoAuthorizer) : INotificationLicenseAuthorizer
{
    public async Task<LicenseAuthorizationResult> AuthorizeAsync(
        NotificationFeature feature,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(feature))
        {
            return LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.ValidationFailed);
        }

        var dongleResult = await AuthorizeSafelyAsync(
            dongleAuthorizer,
            feature,
            cancellationToken);
        if (dongleResult.IsAuthorized ||
            dongleResult.FailureCode == LicenseAuthorizationFailureCodes.ValidationFailed)
        {
            return dongleResult;
        }

        var softwareResult = await AuthorizeSafelyAsync(
            softwareAuthorizer,
            feature,
            cancellationToken);
        if (softwareResult.IsAuthorized)
        {
            return softwareResult;
        }

        return await AuthorizeSafelyAsync(demoAuthorizer, feature, cancellationToken);
    }

    private static async Task<LicenseAuthorizationResult> AuthorizeSafelyAsync(
        INotificationLicenseAuthorizer authorizer,
        NotificationFeature feature,
        CancellationToken cancellationToken)
    {
        try
        {
            return await authorizer.AuthorizeAsync(feature, cancellationToken) ??
                LicenseAuthorizationResult.Denied(
                    LicenseAuthorizationFailureCodes.ValidationFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.ValidationFailed);
        }
    }
}
