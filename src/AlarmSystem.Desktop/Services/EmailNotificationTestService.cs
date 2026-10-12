using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.Desktop.Services;

public sealed class EmailNotificationTestService(
    IEmailSmtpClientFactory clientFactory,
    EmailTemplateProvider templateProvider,
    EmailMessageBuilder messageBuilder) : IEmailNotificationTestService
{
    public async Task<NotificationSendResult> TestConnectionAsync(
        EmailOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
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

        var channel = CreateTransport(options);
        return await channel.SendAsync(notification, cancellationToken);
    }

    private EmailSmtpNotificationChannel CreateTransport(EmailOptions options) => new(
        clientFactory,
        templateProvider,
        messageBuilder,
        options);
}
