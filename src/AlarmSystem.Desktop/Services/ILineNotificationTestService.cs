using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Desktop.Services;

public interface ILineNotificationTestService
{
    Task<NotificationSendResult> SendAsync(
        string groupId,
        string channelAccessToken,
        TimeSpan timeout,
        AlarmNotification notification,
        CancellationToken cancellationToken);
}
