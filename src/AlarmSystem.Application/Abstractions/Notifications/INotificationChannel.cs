namespace AlarmSystem.Application.Abstractions.Notifications;

public interface INotificationChannel
{
    string ChannelName { get; }

    string DestinationReference { get; }

    Task<NotificationSendResult> SendAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken);
}
