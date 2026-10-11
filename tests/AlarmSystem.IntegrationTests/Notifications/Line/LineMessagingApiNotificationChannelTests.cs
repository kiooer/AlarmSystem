using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.IntegrationTests.Notifications.Line;

public sealed class LineMessagingApiNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_PostsExpectedPayloadAndHeadersOnFirstRequest()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"sentMessages\":[{\"id\":\"message-123\"}]}",
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(response);
        });
        var channel = CreateChannel(handler);
        var notification = CreateNotification();

        var result = await channel.SendAsync(notification, CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("message-123", result.ExternalMessageId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.line.me/v2/bot/message/push", request.Uri.AbsoluteUri);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.True(
            string.Equals("unit-test-credential", request.AuthorizationParameter, StringComparison.Ordinal),
            "The Bearer credential did not match the configured test credential.");
        Assert.Equal(notification.EventId.ToString("D"), request.RetryKey);

        var payload = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("test-group", payload["to"]!.GetValue<string>());
        var message = Assert.Single(payload["messages"]!.AsArray())!.AsObject();
        Assert.Equal("flex", message["type"]!.GetValue<string>());
        Assert.Equal("設備警報通知", message["altText"]!.GetValue<string>());
        var bubble = message["contents"]!.AsObject();
        Assert.Equal("bubble", bubble["type"]!.GetValue<string>());
        Assert.Equal("#D99A22", bubble["styles"]!["header"]!["backgroundColor"]!.GetValue<string>());
        Assert.Contains("TEST.TAG", bubble.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_UsesStableEventIdRetryKeyAcrossAttempts()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var channel = CreateChannel(handler);
        var notification = CreateNotification();

        await channel.SendAsync(notification, CancellationToken.None);
        await channel.SendAsync(notification, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request => Assert.Equal(notification.EventId.ToString("D"), request.RetryKey));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"sentMessages\":[]}")]
    public async Task SendAsync_FallsBackToRequestIdWhenSuccessBodyHasNoMessageId(
        string responseBody)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("X-Line-Request-Id", "request-456");
            return Task.FromResult(response);
        });
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("request-456", result.ExternalMessageId);
    }

    [Fact]
    public async Task SendAsync_TreatsConflictAsAcceptedAndUsesAcceptedRequestId()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict);
            response.Headers.Add("X-Line-Accepted-Request-Id", "accepted-789");
            response.Headers.Add("X-Line-Request-Id", "request-789");
            return Task.FromResult(response);
        });
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("accepted-789", result.ExternalMessageId);
    }

    [Fact]
    public async Task SendAsync_PrefersSentMessageIdForConflict()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    "{\"sentMessages\":[{\"id\":\"accepted-message\"}]}",
                    Encoding.UTF8,
                    "application/json"),
            };
            response.Headers.Add("X-Line-Accepted-Request-Id", "accepted-request");
            return Task.FromResult(response);
        });
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("accepted-message", result.ExternalMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_ClassifiesServerErrorsAsRetryable(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("line_http_error", result.ErrorCode);
        Assert.Equal((int)statusCode, result.HttpStatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData((HttpStatusCode)429)]
    public async Task SendAsync_ClassifiesClientErrorsAsPermanent(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("line_http_error", result.ErrorCode);
        Assert.Equal((int)statusCode, result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesTransportFailureAsRetryableWithoutExposingCredential()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Simulated transport failure."));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("line_transport_error", result.ErrorCode);
        Assert.False(
            result.ToString().Contains("unit-test-credential", StringComparison.Ordinal),
            "Notification result exposed the configured credential.");
    }

    [Fact]
    public async Task SendAsync_ClassifiesTimeoutAsRetryable()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new TaskCanceledException("Simulated HTTP timeout."));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("line_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new StubHttpMessageHandler(
            (_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        var channel = CreateChannel(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(CreateNotification(), cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_DoesNotCallHttpWhenDisabled()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var options = new LineOptions
        {
            Enabled = false,
            GroupId = string.Empty,
            ChannelAccessToken = string.Empty,
        };
        var channel = CreateChannel(handler, options);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("channel_disabled", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_DoesNotCallHttpWhenFlexBubbleIsTooLarge()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var channel = CreateChannel(handler);
        var notification = CreateNotification(new string('界', LineFlexMessageRenderer.MaxBubbleJsonBytes));

        var result = await channel.SendAsync(notification, CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("line_flex_message_too_large", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_DoesNotCallHttpWhenTemplateIsUnhealthy()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var channel = new LineMessagingApiNotificationChannel(
            new HttpClient(handler),
            new LineFlexMessageTemplateProvider(Path.Combine(AppContext.BaseDirectory, "missing-flex-template.json")),
            CreateOptions());

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("line_template_unhealthy", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ChannelMetadata_UsesLineNameAndConfiguredGroupId()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var channel = CreateChannel(handler);

        Assert.Equal("Line", channel.ChannelName);
        Assert.Equal("test-group", channel.DestinationReference);
    }

    private static LineMessagingApiNotificationChannel CreateChannel(
        StubHttpMessageHandler handler,
        LineOptions? options = null) => new(
            new HttpClient(handler),
            new LineFlexMessageTemplateProvider(Path.Combine(
                AppContext.BaseDirectory,
                "templates",
                "line-alarm-card.json")),
            options ?? CreateOptions());

    private static LineOptions CreateOptions() => new()
    {
        Enabled = true,
        GroupId = "test-group",
        ChannelAccessToken = "unit-test-credential",
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static AlarmNotification CreateNotification(string message = "Test message") => new(
        Guid.Parse("e0dc4cca-d31c-4c55-97c3-a6e2311fb837"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Ack,
        AlarmSeverity.Medium,
        message,
        new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
        "Test equipment");

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("X-Line-Retry-Key", out var retryKeys)
                    ? retryKeys.Single()
                    : null,
                body));
            return await responder(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? ContentType,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? RetryKey,
        string Body);
}
