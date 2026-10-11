using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Infrastructure.Notifications.Line;

public sealed class LineMessagingApiNotificationChannel : INotificationChannel
{
    public static Uri Endpoint { get; } = new(
        "https://api.line.me/v2/bot/message/push",
        UriKind.Absolute);

    private readonly HttpClient httpClient;
    private readonly LineFlexMessageTemplateProvider templateProvider;
    private readonly LineOptions options;

    public LineMessagingApiNotificationChannel(
        HttpClient httpClient,
        LineFlexMessageTemplateProvider templateProvider,
        LineOptions options)
    {
        this.httpClient = httpClient;
        this.templateProvider = templateProvider;
        this.options = options;
        options.Validate();
    }

    public string ChannelName => "Line";

    public string DestinationReference => options.GroupId;

    public async Task<NotificationSendResult> SendAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return NotificationSendResult.Permanent("channel_disabled");
        }

        JsonObject? bubble;
        try
        {
            if (!templateProvider.TryRender(notification, out bubble))
            {
                return NotificationSendResult.Permanent("line_template_unhealthy");
            }
        }
        catch (LineFlexMessageTooLargeException)
        {
            return NotificationSendResult.Permanent("line_flex_message_too_large");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                to = options.GroupId,
                messages = new[]
                {
                    new
                    {
                        type = "flex",
                        altText = "設備警報通知",
                        contents = bubble,
                    },
                },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            options.ChannelAccessToken);
        request.Headers.Add("X-Line-Retry-Key", notification.EventId.ToString("D"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var externalMessageId = await GetSentMessageIdAsync(response, cancellationToken)
                    ?? GetFirstHeader(response, "X-Line-Request-Id");
                return NotificationSendResult.Success(externalMessageId);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var externalMessageId = await GetSentMessageIdAsync(response, cancellationToken)
                    ?? GetFirstHeader(response, "X-Line-Accepted-Request-Id")
                    ?? GetFirstHeader(response, "X-Line-Request-Id");
                return NotificationSendResult.Success(externalMessageId);
            }

            return statusCode >= 500
                ? NotificationSendResult.Retryable("line_http_error", statusCode)
                : NotificationSendResult.Permanent("line_http_error", statusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NotificationSendResult.Retryable("line_timeout");
        }
        catch (HttpRequestException)
        {
            return NotificationSendResult.Retryable("line_transport_error");
        }
    }

    private static async Task<string?> GetSentMessageIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonNode>(
                cancellationToken: cancellationToken);
            if (body is not JsonObject responseObject ||
                responseObject["sentMessages"] is not JsonArray sentMessages ||
                sentMessages.Count == 0 ||
                sentMessages[0] is not JsonObject sentMessage ||
                sentMessage["id"] is not JsonValue idValue ||
                !idValue.TryGetValue<string>(out var id))
            {
                return null;
            }

            return id;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? GetFirstHeader(HttpResponseMessage response, string headerName) =>
        response.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()
            : null;
}
