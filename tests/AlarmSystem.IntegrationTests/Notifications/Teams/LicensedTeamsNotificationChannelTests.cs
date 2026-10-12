using System.Net;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.IntegrationTests.Notifications.Teams;

public sealed class LicensedTeamsNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_WhenTeamsFeatureIsAuthorized_PreservesTransportContract()
    {
        var handler = new CountingHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (feature, _) =>
            {
                Assert.Equal(NotificationFeature.Teams, feature);
                return Task.FromResult(LicenseAuthorizationResult.Authorized);
            },
        };
        var channel = CreateLicensedChannel(handler, authorizer);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://example.invalid/teams-webhook", handler.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SendAsync_WhenTeamsFeatureIsNotLicensed_DoesNotRenderOrCallHttp()
    {
        var handler = new CountingHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, _) => Task.FromResult(LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.FeatureNotLicensed)),
        };
        var channel = CreateLicensedChannel(handler, authorizer);

        var result = await channel.SendAsync(
            CreateNotification(tag: " "),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.FeatureNotLicensed, result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenAuthorizationIsCancelledByCaller_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new CountingHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<LicenseAuthorizationResult>(token);
            },
        };
        var channel = CreateLicensedChannel(handler, authorizer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(CreateNotification(), cancellation.Token));
        Assert.Equal(0, handler.CallCount);
    }

    private static LicensedNotificationChannel CreateLicensedChannel(
        CountingHttpMessageHandler handler,
        INotificationLicenseAuthorizer authorizer)
    {
        var transport = new TeamsWebhookNotificationChannel(
            new HttpClient(handler),
            new TeamsCardTemplateProvider(Path.Combine(
                AppContext.BaseDirectory,
                "templates",
                "teams-alarm-card.json")),
            new TeamsOptions
            {
                Enabled = true,
                Endpoint = new Uri("https://example.invalid/teams-webhook"),
                DestinationReference = "test-destination",
                Timeout = TimeSpan.FromSeconds(5),
            });
        return new LicensedNotificationChannel(
            transport,
            authorizer,
            NotificationFeature.Teams);
    }

    private static AlarmNotification CreateNotification(string tag = "TEST.TAG") => new(
        Guid.Parse("60d4d7e7-6b82-43e3-91f5-59df839651de"),
        tag,
        "Test area",
        AlarmEventType.Ack,
        AlarmSeverity.Medium,
        "Test message",
        new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(8)));

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            return responder(request, cancellationToken);
        }
    }
}
