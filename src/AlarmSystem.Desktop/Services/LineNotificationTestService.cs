using System.Net.Http;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.Desktop.Services;

public sealed class LineNotificationTestService(
    IHttpClientFactory httpClientFactory,
    LineAlarmTextRenderer renderer) : ILineNotificationTestService
{
    public const string HttpClientName = "LineNotificationTest";

    public async Task<NotificationSendResult> SendAsync(
        string groupId,
        string channelAccessToken,
        TimeSpan timeout,
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        var options = new LineOptions
        {
            Enabled = true,
            GroupId = groupId,
            ChannelAccessToken = channelAccessToken,
            Timeout = timeout,
        };
        options.Validate();

        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = options.Timeout;
        var channel = new LineMessagingApiNotificationChannel(httpClient, renderer, options);

        return await channel.SendAsync(notification, cancellationToken);
    }
}
