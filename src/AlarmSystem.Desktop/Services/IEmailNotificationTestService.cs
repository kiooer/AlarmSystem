using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.Desktop.Services;

public interface IEmailNotificationTestService
{
    Task<EmailSmtpExecutionResult> TestConnectionAsync(
        EmailOptions options,
        CancellationToken cancellationToken);

    Task<EmailSmtpExecutionResult> SendAsync(
        EmailOptions options,
        AlarmNotification notification,
        CancellationToken cancellationToken);
}
