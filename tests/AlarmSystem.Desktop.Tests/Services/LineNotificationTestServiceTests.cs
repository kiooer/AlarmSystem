using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.Desktop.Tests.Services;

public sealed class LineNotificationTestServiceTests
{
    [Fact]
    public void Constructor_DoesNotAcceptFormalLicenseAuthorizer()
    {
        var constructor = Assert.Single(typeof(LineNotificationTestService).GetConstructors());

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(INotificationLicenseAuthorizer));
    }

    [Fact]
    public async Task SendAsync_UsesOfficialChannelRequestContract()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"sentMessages\":[{\"id\":\"desktop-message-1\"}]}",
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(response);
        });
        var factory = new StubHttpClientFactory(handler);
        var service = CreateService(factory);
        var notification = CreateNotification();

        var result = await service.SendAsync(
            "desktop-test-group",
            "desktop-test-credential",
            TimeSpan.FromSeconds(12),
            notification,
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("desktop-message-1", result.ExternalMessageId);
        Assert.Equal(LineNotificationTestService.HttpClientName, factory.RequestedName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(LineMessagingApiNotificationChannel.Endpoint, request.Uri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.True(
            string.Equals(
                "desktop-test-credential",
                request.AuthorizationParameter,
                StringComparison.Ordinal),
            "The Authorization credential did not match the test input.");
        Assert.Equal(notification.EventId.ToString("D"), request.RetryKey);
        var payload = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("desktop-test-group", payload["to"]!.GetValue<string>());
        var message = Assert.Single(payload["messages"]!.AsArray())!.AsObject();
        Assert.Equal("flex", message["type"]!.GetValue<string>());
        Assert.Equal("設備警報通知", message["altText"]!.GetValue<string>());
        Assert.Equal("bubble", message["contents"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, NotificationSendStatus.RetryableFailure)]
    [InlineData(HttpStatusCode.Unauthorized, NotificationSendStatus.PermanentFailure)]
    public async Task SendAsync_UsesOfficialChannelHttpClassification(
        HttpStatusCode httpStatusCode,
        NotificationSendStatus expectedStatus)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(httpStatusCode)));
        var service = CreateService(new StubHttpClientFactory(handler));

        var result = await service.SendAsync(
            "desktop-test-group",
            "desktop-test-credential",
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("line_http_error", result.ErrorCode);
        Assert.Equal((int)httpStatusCode, result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_AppliesRequestedTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(new StubHttpClientFactory(handler));

        var result = await service.SendAsync(
            "desktop-test-group",
            "desktop-test-credential",
            TimeSpan.FromMilliseconds(50),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("line_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_PropagatesCallerCancellation()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(new StubHttpClientFactory(handler));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendAsync(
            "desktop-test-group",
            "desktop-test-credential",
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_ResultDoesNotExposeCredential()
    {
        const string sensitiveCredential = "credential-that-must-remain-private";
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Simulated transport failure."));
        var service = CreateService(new StubHttpClientFactory(handler));

        var result = await service.SendAsync(
            "desktop-test-group",
            sensitiveCredential,
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            CancellationToken.None);

        Assert.False(
            result.ToString().Contains(sensitiveCredential, StringComparison.Ordinal),
            "The notification result exposed the credential.");
    }

    [Fact]
    public async Task SendAsync_ValidatesOptionsBeforeCallingHttp()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var service = CreateService(new StubHttpClientFactory(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(
            "   ",
            "desktop-test-credential",
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    private static LineNotificationTestService CreateService(IHttpClientFactory factory) =>
        new(factory, new LineFlexMessageTemplateProvider(Path.Combine(
            AppContext.BaseDirectory,
            "templates",
            "line-alarm-card.json")));

    private static AlarmNotification CreateNotification() => new(
        Guid.Parse("89e02f25-306c-40f1-a39c-a4ff4536dcce"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "Desktop test message",
        new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.FromHours(8)),
        "Test equipment");

    private sealed class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

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
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? RetryKey,
        string Body);
}
