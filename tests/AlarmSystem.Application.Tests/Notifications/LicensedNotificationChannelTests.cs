using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Application.Tests.Notifications;

public sealed class LicensedNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_WhenAuthorized_DelegatesToInnerChannel()
    {
        var inner = new StubNotificationChannel();
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (feature, _) =>
            {
                Assert.Equal(NotificationFeature.Line, feature);
                return Task.FromResult(LicenseAuthorizationResult.Authorized);
            },
        };
        var channel = new LicensedNotificationChannel(
            inner,
            authorizer,
            NotificationFeature.Line);
        var notification = CreateNotification();

        var result = await channel.SendAsync(notification, CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(1, inner.CallCount);
        Assert.Same(notification, inner.LastNotification);
    }

    [Theory]
    [InlineData(LicenseAuthorizationFailureCodes.FeatureNotLicensed)]
    [InlineData(LicenseAuthorizationFailureCodes.Expired)]
    [InlineData(LicenseAuthorizationFailureCodes.DongleUnavailable)]
    [InlineData(LicenseAuthorizationFailureCodes.ValidationFailed)]
    public async Task SendAsync_WhenDenied_DoesNotCallInnerChannel(string failureCode)
    {
        var inner = new StubNotificationChannel();
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, _) => Task.FromResult(
                LicenseAuthorizationResult.Denied(failureCode)),
        };
        var channel = new LicensedNotificationChannel(
            inner,
            authorizer,
            NotificationFeature.Line);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(failureCode, result.ErrorCode);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenAuthorizerThrows_FailsClosedWithoutCallingInnerChannel()
    {
        var inner = new StubNotificationChannel();
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, _) => throw new InvalidOperationException("Sensitive vendor detail."),
        };
        var channel = new LicensedNotificationChannel(
            inner,
            authorizer,
            NotificationFeature.Teams);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.ErrorCode);
        Assert.DoesNotContain("Sensitive vendor detail", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenAuthorizerCancelsWithoutCallerCancellation_FailsClosed()
    {
        var inner = new StubNotificationChannel();
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, _) => throw new OperationCanceledException("Reader timeout."),
        };
        var channel = new LicensedNotificationChannel(
            inner,
            authorizer,
            NotificationFeature.Teams);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenCallerCancelsAuthorization_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = new StubNotificationChannel();
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<LicenseAuthorizationResult>(token);
            },
        };
        var channel = new LicensedNotificationChannel(
            inner,
            authorizer,
            NotificationFeature.Line);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(CreateNotification(), cancellation.Token));
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_AuthorizesLineAndTeamsIndependently()
    {
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (feature, _) => Task.FromResult(
                feature == NotificationFeature.Line
                    ? LicenseAuthorizationResult.Authorized
                    : LicenseAuthorizationResult.Denied(
                        LicenseAuthorizationFailureCodes.FeatureNotLicensed)),
        };
        var lineInner = new StubNotificationChannel("Line", "line-destination");
        var teamsInner = new StubNotificationChannel("Teams", "teams-destination");
        var line = new LicensedNotificationChannel(
            lineInner,
            authorizer,
            NotificationFeature.Line);
        var teams = new LicensedNotificationChannel(
            teamsInner,
            authorizer,
            NotificationFeature.Teams);

        var lineResult = await line.SendAsync(CreateNotification(), CancellationToken.None);
        var teamsResult = await teams.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, lineResult.Status);
        Assert.Equal(NotificationSendStatus.PermanentFailure, teamsResult.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.FeatureNotLicensed, teamsResult.ErrorCode);
        Assert.Equal(1, lineInner.CallCount);
        Assert.Equal(0, teamsInner.CallCount);
    }

    [Fact]
    public async Task SendAsync_AuthorizesEmailIndependentlyFromLineAndTeams()
    {
        var authorizer = new StubLicenseAuthorizer
        {
            Handler = (feature, _) => Task.FromResult(
                feature == NotificationFeature.Email
                    ? LicenseAuthorizationResult.Authorized
                    : LicenseAuthorizationResult.Denied(
                        LicenseAuthorizationFailureCodes.FeatureNotLicensed)),
        };
        var emailInner = new StubNotificationChannel("Email", "email-group");
        var lineInner = new StubNotificationChannel("Line", "line-destination");
        var teamsInner = new StubNotificationChannel("Teams", "teams-destination");
        var email = new LicensedNotificationChannel(
            emailInner,
            authorizer,
            NotificationFeature.Email);
        var line = new LicensedNotificationChannel(
            lineInner,
            authorizer,
            NotificationFeature.Line);
        var teams = new LicensedNotificationChannel(
            teamsInner,
            authorizer,
            NotificationFeature.Teams);

        var emailResult = await email.SendAsync(CreateNotification(), CancellationToken.None);
        var lineResult = await line.SendAsync(CreateNotification(), CancellationToken.None);
        var teamsResult = await teams.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, emailResult.Status);
        Assert.Equal(NotificationSendStatus.PermanentFailure, lineResult.Status);
        Assert.Equal(NotificationSendStatus.PermanentFailure, teamsResult.Status);
        Assert.Equal(1, emailInner.CallCount);
        Assert.Equal(0, lineInner.CallCount);
        Assert.Equal(0, teamsInner.CallCount);
    }

    [Fact]
    public void Metadata_ComesFromInnerChannel()
    {
        var channel = new LicensedNotificationChannel(
            new StubNotificationChannel("Line", "masked-by-consumer"),
            new StubLicenseAuthorizer(),
            NotificationFeature.Line);

        Assert.Equal("Line", channel.ChannelName);
        Assert.Equal("masked-by-consumer", channel.DestinationReference);
    }

    private static AlarmNotification CreateNotification() => new(
        Guid.Parse("3727b40a-5f0e-4d75-9a49-d24fdba85d42"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "Test message",
        new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(8)));

    private sealed class StubLicenseAuthorizer : INotificationLicenseAuthorizer
    {
        public Func<NotificationFeature, CancellationToken, Task<LicenseAuthorizationResult>> Handler
        { get; init; } = (_, _) => Task.FromResult(LicenseAuthorizationResult.Authorized);

        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken) => Handler(feature, cancellationToken);
    }

    private sealed class StubNotificationChannel(
        string channelName = "Stub",
        string destinationReference = "stub-destination") : INotificationChannel
    {
        public string ChannelName => channelName;

        public string DestinationReference => destinationReference;

        public int CallCount { get; private set; }

        public AlarmNotification? LastNotification { get; private set; }

        public Task<NotificationSendResult> SendAsync(
            AlarmNotification notification,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastNotification = notification;
            return Task.FromResult(NotificationSendResult.Success("stub-message"));
        }
    }
}
