using System.Net.Http;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.Desktop.Services;

public sealed class TeamsNotificationTestService(
    IHttpClientFactory httpClientFactory,
    TeamsCardTemplateProvider templateProvider) : ITeamsNotificationTestService
{
    public const string HttpClientName = "TeamsNotificationTest";

    public async Task<NotificationSendResult> SendAsync(
        string webhookUrl,
        string destinationReference,
        TimeSpan timeout,
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        Uri.TryCreate(webhookUrl, UriKind.Absolute, out var endpoint);
        var options = new TeamsOptions
        {
            Enabled = true,
            Endpoint = endpoint,
            DestinationReference = destinationReference,
            Timeout = timeout,
        };
        options.Validate();

        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = options.Timeout;
        var channel = new TeamsWebhookNotificationChannel(httpClient, templateProvider, options);

        return await channel.SendAsync(notification, cancellationToken);
    }
}
