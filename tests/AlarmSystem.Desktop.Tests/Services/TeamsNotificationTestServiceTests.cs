using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.Desktop.Tests.Services;

public sealed class TeamsNotificationTestServiceTests
{
    private const string TestEndpoint = "https://example.invalid/teams-test-hook";

    [Fact]
    public void Constructor_DoesNotDependOnLicenseAuthorizer()
    {
        var constructor = Assert.Single(typeof(TeamsNotificationTestService).GetConstructors());

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(INotificationLicenseAuthorizer));
    }

    [Fact]
    public async Task SendAsync_UsesNamedClientAndOfficialTeamsEnvelope()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("request-id", "desktop-teams-request-1");
            return Task.FromResult(response);
        });
        var factory = new StubHttpClientFactory(handler);
        var service = CreateService(factory);

        var result = await service.SendAsync(
            TestEndpoint,
            "TeamsManualTest",
            TimeSpan.FromSeconds(12),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal("desktop-teams-request-1", result.ExternalMessageId);
        Assert.Equal(TeamsNotificationTestService.HttpClientName, factory.RequestedName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TestEndpoint, request.Uri.AbsoluteUri);
        var payload = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("message", payload["type"]!.GetValue<string>());
        var attachment = Assert.Single(payload["attachments"]!.AsArray())!.AsObject();
        Assert.Equal(
            "application/vnd.microsoft.card.adaptive",
            attachment["contentType"]!.GetValue<string>());
        Assert.Equal("AdaptiveCard", attachment["content"]!["type"]!.GetValue<string>());
        Assert.Contains("TEST.TAG", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, NotificationSendStatus.Succeeded, null)]
    [InlineData(HttpStatusCode.BadRequest, NotificationSendStatus.PermanentFailure, "teams_http_error")]
    [InlineData(HttpStatusCode.RequestTimeout, NotificationSendStatus.RetryableFailure, "teams_http_error")]
    [InlineData((HttpStatusCode)429, NotificationSendStatus.RetryableFailure, "teams_http_error")]
    [InlineData(HttpStatusCode.InternalServerError, NotificationSendStatus.RetryableFailure, "teams_http_error")]
    public async Task SendAsync_PreservesExistingHttpClassification(
        HttpStatusCode httpStatusCode,
        NotificationSendStatus expectedStatus,
        string? expectedErrorCode)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(httpStatusCode)));
        var service = CreateService(new StubHttpClientFactory(handler));

        var result = await service.SendAsync(
            TestEndpoint,
            "TeamsManualTest",
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Equal(
            httpStatusCode == HttpStatusCode.OK ? null : (int)httpStatusCode,
            result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesTimeoutWithoutExposingEndpoint()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(new StubHttpClientFactory(handler));

        var result = await service.SendAsync(
            TestEndpoint,
            "TeamsManualTest",
            TimeSpan.FromMilliseconds(50),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("teams_timeout", result.ErrorCode);
        Assert.DoesNotContain(TestEndpoint, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ClassifiesTransportFailureWithoutExposingEndpoint()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Simulated desktop transport failure."));
        var service = CreateService(new StubHttpClientFactory(handler));

        var result = await service.SendAsync(
            TestEndpoint,
            "TeamsManualTest",
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("teams_transport_error", result.ErrorCode);
        Assert.DoesNotContain(TestEndpoint, result.ToString(), StringComparison.Ordinal);
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
            TestEndpoint,
            "TeamsManualTest",
            TimeSpan.FromSeconds(10),
            CreateNotification(),
            cancellation.Token));
    }

    [Theory]
    [InlineData("http://example.invalid/test-hook", "TeamsManualTest", 10)]
    [InlineData("relative/test-hook", "TeamsManualTest", 10)]
    [InlineData(TestEndpoint, " ", 10)]
    [InlineData(TestEndpoint, "TeamsManualTest", 121)]
    public async Task SendAsync_ValidatesOptionsBeforeCallingHttp(
        string endpoint,
        string destinationReference,
        int timeoutSeconds)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var service = CreateService(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(
            endpoint,
            destinationReference,
            TimeSpan.FromSeconds(timeoutSeconds),
            CreateNotification(),
            CancellationToken.None));

        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(endpoint, exception.Message, StringComparison.Ordinal);
    }

    private static TeamsNotificationTestService CreateService(IHttpClientFactory factory) =>
        new(factory, new TeamsCardTemplateProvider(Path.Combine(
            AppContext.BaseDirectory,
            "templates",
            "teams-alarm-card.json")));

    private static AlarmNotification CreateNotification() => new(
        Guid.Parse("7f68eb2e-62dd-4490-99a1-4bb505f699ef"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "Desktop Teams test message",
        new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.FromHours(8)),
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
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return await responder(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);
}
