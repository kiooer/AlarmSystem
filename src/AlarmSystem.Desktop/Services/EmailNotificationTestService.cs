using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.Desktop.Services;

public sealed class EmailNotificationTestService(
    IEmailSmtpClientFactory clientFactory,
    EmailTemplateProvider templateProvider,
    EmailMessageBuilder messageBuilder,
    INotificationLicenseAuthorizer licenseAuthorizer) : IEmailNotificationTestService
{
    public async Task<LicenseAuthorizationResult> GetLicenseStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await licenseAuthorizer.AuthorizeAsync(
                NotificationFeature.Email,
                cancellationToken);
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

    public async Task<NotificationSendResult> TestConnectionAsync(
        EmailOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var authorization = await GetLicenseStatusAsync(cancellationToken);
        if (!authorization.IsAuthorized)
        {
            return NotificationSendResult.Permanent(
                authorization.FailureCode ?? LicenseAuthorizationFailureCodes.ValidationFailed);
        }

        var channel = CreateTransport(options);
        return await channel.TestConnectionAsync(cancellationToken);
    }

    public async Task<NotificationSendResult> SendAsync(
        EmailOptions options,
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(notification);

        var channel = new LicensedNotificationChannel(
            CreateTransport(options),
            licenseAuthorizer,
            NotificationFeature.Email);
        return await channel.SendAsync(notification, cancellationToken);
    }

    private EmailSmtpNotificationChannel CreateTransport(EmailOptions options) => new(
        clientFactory,
        templateProvider,
        messageBuilder,
        options);
}
