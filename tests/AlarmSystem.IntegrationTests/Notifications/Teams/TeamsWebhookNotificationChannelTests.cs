using System.Net;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.IntegrationTests.Notifications.Teams;

public sealed class TeamsWebhookNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_PostsAdaptiveCardEnvelopeAndReturnsSuccess()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("request-id", "request-123");
            return Task.FromResult(response);
        });
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("request-123", result.ExternalMessageId);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.invalid/teams-webhook", handler.Request.RequestUri!.AbsoluteUri);

        var payload = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        Assert.Equal("message", payload["type"]!.GetValue<string>());
        var attachment = payload["attachments"]!.AsArray().Single()!.AsObject();
        Assert.Equal(
            "application/vnd.microsoft.card.adaptive",
            attachment["contentType"]!.GetValue<string>());
        Assert.Equal("AdaptiveCard", attachment["content"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SendAsync_ClassifiesTransientHttpErrorsAsRetryable(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal((int)statusCode, result.HttpStatusCode);
        Assert.Equal("teams_http_error", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesNonRetryableHttpErrorAsPermanent()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(400, result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesTransportFailureAsRetryable()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Simulated failure containing no real endpoint."));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("teams_transport_error", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesTimeoutAsRetryable()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new TaskCanceledException("Simulated HTTP timeout."));
        var channel = CreateChannel(handler);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("teams_timeout", result.ErrorCode);
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
    public async Task SendAsync_DoesNotCallHttpWhenChannelIsDisabled()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var options = new TeamsOptions
        {
            Enabled = false,
            Endpoint = new Uri("https://example.invalid/teams-webhook"),
            DestinationReference = "test-destination",
            TemplatePath = "templates/teams-alarm-card.json",
            Timeout = TimeSpan.FromSeconds(5),
        };
        var channel = CreateChannel(handler, options);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("channel_disabled", result.ErrorCode);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SendAsync_DoesNotCallHttpWhenTemplateIsUnhealthy()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var provider = new TeamsCardTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "missing-template.json"));
        var channel = new TeamsWebhookNotificationChannel(
            new HttpClient(handler),
            provider,
            CreateOptions());

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("teams_template_unhealthy", result.ErrorCode);
        Assert.Null(handler.Request);
    }

    private static TeamsWebhookNotificationChannel CreateChannel(
        StubHttpMessageHandler handler,
        TeamsOptions? options = null)
    {
        var provider = new TeamsCardTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "teams-alarm-card.json"));
        return new TeamsWebhookNotificationChannel(
            new HttpClient(handler),
            provider,
            options ?? CreateOptions());
    }

    private static TeamsOptions CreateOptions() => new()
    {
        Enabled = true,
        Endpoint = new Uri("https://example.invalid/teams-webhook"),
        DestinationReference = "test-destination",
        TemplatePath = "templates/teams-alarm-card.json",
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static AlarmNotification CreateNotification() => new(
        Guid.Parse("e0dc4cca-d31c-4c55-97c3-a6e2311fb837"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Ack,
        AlarmSeverity.Medium,
        "Test message",
        new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await responder(request, cancellationToken);
        }
    }
}
