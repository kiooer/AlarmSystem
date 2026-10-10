using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Infrastructure.Notifications.Teams;

public sealed class TeamsWebhookNotificationChannel : INotificationChannel
{
    private readonly HttpClient httpClient;
    private readonly TeamsCardTemplateProvider templateProvider;
    private readonly TeamsOptions options;

    public TeamsWebhookNotificationChannel(
        HttpClient httpClient,
        TeamsCardTemplateProvider templateProvider,
        TeamsOptions options)
    {
        this.httpClient = httpClient;
        this.templateProvider = templateProvider;
        this.options = options;
        options.Validate();
    }

    public string ChannelName => "Teams";

    public string DestinationReference => options.DestinationReference;

    public async Task<NotificationSendResult> SendAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return NotificationSendResult.Permanent("channel_disabled");
        }

        if (!templateProvider.TryRender(notification, out var card))
        {
            return NotificationSendResult.Retryable(
                templateProvider.FailureCode ?? "teams_template_unhealthy");
        }

        var payload = CreateWebhookPayload(card!);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                options.Endpoint,
                payload,
                cancellationToken);

            var statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return NotificationSendResult.Success(GetExternalMessageId(response));
            }

            return IsRetryable(response.StatusCode)
                ? NotificationSendResult.Retryable("teams_http_error", statusCode)
                : NotificationSendResult.Permanent("teams_http_error", statusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NotificationSendResult.Retryable("teams_timeout");
        }
        catch (HttpRequestException)
        {
            return NotificationSendResult.Retryable("teams_transport_error");
        }
    }

    private static JsonObject CreateWebhookPayload(JsonObject card) => new()
    {
        ["type"] = "message",
        ["attachments"] = new JsonArray
        {
            new JsonObject
            {
                ["contentType"] = "application/vnd.microsoft.card.adaptive",
                ["contentUrl"] = null,
                ["content"] = card,
            },
        },
    };

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 ||
        (int)statusCode >= 500;

    private static string? GetExternalMessageId(HttpResponseMessage response)
    {
        var headerNames = new[] { "request-id", "x-ms-request-id" };
        foreach (var headerName in headerNames)
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }
}
