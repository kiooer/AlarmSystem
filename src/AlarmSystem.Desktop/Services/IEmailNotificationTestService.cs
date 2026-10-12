using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.Desktop.Services;

public interface IEmailNotificationTestService
{
    Task<LicenseAuthorizationResult> GetLicenseStatusAsync(
        CancellationToken cancellationToken);

    Task<NotificationSendResult> TestConnectionAsync(
        EmailOptions options,
        CancellationToken cancellationToken);

    Task<NotificationSendResult> SendAsync(
        EmailOptions options,
        AlarmNotification notification,
        CancellationToken cancellationToken);
}
