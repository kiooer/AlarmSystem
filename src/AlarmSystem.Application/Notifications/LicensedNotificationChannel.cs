using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Application.Notifications;

public sealed class LicensedNotificationChannel : INotificationChannel
{
    private readonly INotificationChannel innerChannel;
    private readonly INotificationLicenseAuthorizer authorizer;
    private readonly NotificationFeature feature;

    public LicensedNotificationChannel(
        INotificationChannel innerChannel,
        INotificationLicenseAuthorizer authorizer,
        NotificationFeature feature)
    {
        ArgumentNullException.ThrowIfNull(innerChannel);
        ArgumentNullException.ThrowIfNull(authorizer);
        if (!Enum.IsDefined(feature))
        {
            throw new ArgumentOutOfRangeException(nameof(feature));
        }

        this.innerChannel = innerChannel;
        this.authorizer = authorizer;
        this.feature = feature;
    }

    public string ChannelName => innerChannel.ChannelName;

    public string DestinationReference => innerChannel.DestinationReference;

    public async Task<NotificationSendResult> SendAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LicenseAuthorizationResult? authorization;
        try
        {
            authorization = await authorizer.AuthorizeAsync(feature, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return NotificationSendResult.Permanent(
                LicenseAuthorizationFailureCodes.ValidationFailed);
        }

        if (authorization is null)
        {
            return NotificationSendResult.Permanent(
                LicenseAuthorizationFailureCodes.ValidationFailed);
        }

        if (!authorization.IsAuthorized)
        {
            return NotificationSendResult.Permanent(
                authorization.FailureCode ?? LicenseAuthorizationFailureCodes.ValidationFailed);
        }

        return await innerChannel.SendAsync(notification, cancellationToken);
    }
}
