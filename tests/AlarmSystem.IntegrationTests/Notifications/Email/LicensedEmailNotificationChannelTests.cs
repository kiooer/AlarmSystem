using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Email;
using MimeKit;

namespace AlarmSystem.IntegrationTests.Notifications.Email;

public sealed class LicensedEmailNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_WhenEmailFeatureIsAuthorized_EntersEmailAdapter()
    {
        var smtp = new CountingSmtpClient();
        var channel = CreateLicensedChannel(
            smtp,
            (_, _) => Task.FromResult(LicenseAuthorizationResult.Authorized));

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(1, smtp.SendCount);
    }

    [Theory]
    [InlineData(LicenseAuthorizationFailureCodes.FeatureNotLicensed)]
    [InlineData(LicenseAuthorizationFailureCodes.DongleUnavailable)]
    [InlineData(LicenseAuthorizationFailureCodes.Expired)]
    [InlineData(LicenseAuthorizationFailureCodes.ValidationFailed)]
    public async Task SendAsync_WhenLicenseIsDenied_DoesNotCreateSmtpClient(string failureCode)
    {
        var smtp = new CountingSmtpClient();
        var factory = new CountingSmtpClientFactory(smtp);
        var channel = CreateLicensedChannel(
            factory,
            (_, _) => Task.FromResult(LicenseAuthorizationResult.Denied(failureCode)));

        var result = await channel.SendAsync(CreateNotification(tag: " "), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(failureCode, result.ErrorCode);
        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(0, smtp.SendCount);
    }

    [Fact]
    public async Task SendAsync_WhenAuthorizerThrows_FailsClosedWithoutSmtp()
    {
        var smtp = new CountingSmtpClient();
        var factory = new CountingSmtpClientFactory(smtp);
        var channel = CreateLicensedChannel(
            factory,
            (_, _) => throw new InvalidOperationException("Private Dongle response."));

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task SendAsync_WhenLicenseCheckIsCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var smtp = new CountingSmtpClient();
        var channel = CreateLicensedChannel(
            smtp,
            (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<LicenseAuthorizationResult>(token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(CreateNotification(), cancellation.Token));
        Assert.Equal(0, smtp.SendCount);
    }

    private static LicensedNotificationChannel CreateLicensedChannel(
        CountingSmtpClient smtp,
        Func<NotificationFeature, CancellationToken, Task<LicenseAuthorizationResult>> authorize) =>
        CreateLicensedChannel(new CountingSmtpClientFactory(smtp), authorize);

    private static LicensedNotificationChannel CreateLicensedChannel(
        IEmailSmtpClientFactory factory,
        Func<NotificationFeature, CancellationToken, Task<LicenseAuthorizationResult>> authorize)
    {
        var builder = new EmailMessageBuilder();
        var transport = new EmailSmtpNotificationChannel(
            factory,
            new EmailTemplateProvider(Path.Combine(
                AppContext.BaseDirectory,
                "templates",
                "email-alarm.html"), builder),
            builder,
            new EmailOptions
            {
                Enabled = true,
                Host = "smtp.example.invalid",
                Port = 587,
                SecurityMode = EmailSecurityMode.StartTls,
                AuthenticationMode = EmailAuthenticationMode.Password,
                Username = "smtp-user",
                Password = "unit-test-password",
                SenderAddress = "alarm@example.invalid",
                Recipients = ["operator@example.invalid"],
                DestinationReference = "Operations",
            });
        return new LicensedNotificationChannel(
            transport,
            new StubAuthorizer(authorize),
            NotificationFeature.Email);
    }

    private static AlarmNotification CreateNotification(string tag = "TEST.TAG") => new(
        Guid.Parse("79b73b31-520d-4c40-b6c4-dacb3560755d"),
        tag,
        "Test area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "Test message",
        new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.FromHours(8)),
        "Test equipment");

    private sealed class StubAuthorizer(
        Func<NotificationFeature, CancellationToken, Task<LicenseAuthorizationResult>> authorize)
        : INotificationLicenseAuthorizer
    {
        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken)
        {
            Assert.Equal(NotificationFeature.Email, feature);
            return authorize(feature, cancellationToken);
        }
    }

    private sealed class CountingSmtpClientFactory(CountingSmtpClient client)
        : IEmailSmtpClientFactory
    {
        public int CreateCount { get; private set; }

        public IEmailSmtpClient Create()
        {
            CreateCount++;
            return client;
        }
    }

    private sealed class CountingSmtpClient : IEmailSmtpClient
    {
        public int SendCount { get; private set; }

        public Task ConnectAsync(
            string host,
            int port,
            EmailSecurityMode securityMode,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> SendAsync(
            MimeMessage message,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult("Accepted");
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
