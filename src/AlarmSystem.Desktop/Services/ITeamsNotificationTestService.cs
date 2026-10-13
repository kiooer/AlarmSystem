using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Desktop.Services;

public interface ITeamsNotificationTestService
{
    Task<NotificationSendResult> SendAsync(
        string webhookUrl,
        string destinationReference,
        TimeSpan timeout,
        AlarmNotification notification,
        CancellationToken cancellationToken);
}
