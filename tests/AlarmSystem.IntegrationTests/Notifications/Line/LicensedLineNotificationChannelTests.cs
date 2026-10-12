using System.Net;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.IntegrationTests.Notifications.Line;

public sealed class LicensedLineNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_WhenLineFeatureIsAuthorized_PreservesTransportContract()
    {
        var handler = new CountingHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (feature, _) =>
            {
                Assert.Equal(NotificationFeature.Line, feature);
                return Task.FromResult(LicenseAuthorizationResult.Authorized);
            },
        };
        var channel = CreateLicensedChannel(handler, authorizer);
        var notification = CreateNotification();

        var result = await channel.SendAsync(notification, CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(LineMessagingApiNotificationChannel.Endpoint, handler.RequestUri);
        Assert.Equal(notification.EventId.ToString("D"), handler.RetryKey);
    }

    [Theory]
    [InlineData(LicenseAuthorizationFailureCodes.FeatureNotLicensed)]
    [InlineData(LicenseAuthorizationFailureCodes.Expired)]
    [InlineData(LicenseAuthorizationFailureCodes.DongleUnavailable)]
    [InlineData(LicenseAuthorizationFailureCodes.ValidationFailed)]
    public async Task SendAsync_WhenLicenseIsDenied_DoesNotRenderOrCallHttp(string failureCode)
    {
        var handler = new CountingHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, _) => Task.FromResult(
                LicenseAuthorizationResult.Denied(failureCode)),
        };
        var channel = CreateLicensedChannel(handler, authorizer);
        var invalidNotification = CreateNotification(tag: " ");

        var result = await channel.SendAsync(invalidNotification, CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(failureCode, result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenLicenseReaderThrows_DoesNotRenderOrCallHttp()
    {
        var handler = new CountingHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, _) => throw new InvalidOperationException("Vendor response must stay private."),
        };
        var channel = CreateLicensedChannel(handler, authorizer);

        var result = await channel.SendAsync(
            CreateNotification(tag: " "),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    private static LicensedNotificationChannel CreateLicensedChannel(
        CountingHttpMessageHandler handler,
        INotificationLicenseAuthorizer authorizer)
    {
        var transport = new LineMessagingApiNotificationChannel(
            new HttpClient(handler),
            new LineFlexMessageTemplateProvider(Path.Combine(
                AppContext.BaseDirectory,
                "templates",
                "line-alarm-card.json")),
            new LineOptions
            {
                Enabled = true,
                GroupId = "test-group",
                ChannelAccessToken = "unit-test-credential",
                Timeout = TimeSpan.FromSeconds(5),
            });
        return new LicensedNotificationChannel(
            transport,
            authorizer,
            NotificationFeature.Line);
    }

    private static AlarmNotification CreateNotification(string tag = "TEST.TAG") => new(
        Guid.Parse("c9bd4aa7-7924-4ca6-bb53-341790d39816"),
        tag,
        "Test area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "Test message",
        new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(8)),
        "Test equipment");

    private sealed class StubLicenseAuthorizer : INotificationLicenseAuthorizer
    {
        public required Func<
            NotificationFeature,
            CancellationToken,
            Task<LicenseAuthorizationResult>> Handler
        { get; init; }

        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken) => Handler(feature, cancellationToken);
    }

    private sealed class CountingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? RetryKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            RetryKey = request.Headers.TryGetValues("X-Line-Retry-Key", out var retryKeys)
                ? retryKeys.Single()
                : null;
            return responder(request, cancellationToken);
        }
    }
}
