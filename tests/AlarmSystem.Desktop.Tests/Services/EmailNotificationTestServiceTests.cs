using System.IO;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Email;
using MimeKit;

namespace AlarmSystem.Desktop.Tests.Services;

public sealed class EmailNotificationTestServiceTests
{
    [Fact]
    public async Task SendAsync_WhenAuthorized_UsesFormalEmailTransport()
    {
        var client = new StubEmailSmtpClient();
        var authorizer = new StubAuthorizer(LicenseAuthorizationResult.Authorized);
        var service = CreateService(client, authorizer);

        var result = await service.SendAsync(
            CreateOptions(),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(NotificationFeature.Email, authorizer.LastFeature);
        Assert.Equal(["connect", "authenticate", "send", "disconnect"], client.Calls);
        Assert.Equal(2, client.Message!.To.Count);
    }

    [Theory]
    [InlineData(LicenseAuthorizationFailureCodes.FeatureNotLicensed)]
    [InlineData(LicenseAuthorizationFailureCodes.DongleUnavailable)]
    [InlineData(LicenseAuthorizationFailureCodes.Expired)]
    [InlineData(LicenseAuthorizationFailureCodes.ValidationFailed)]
    public async Task SendAsync_WhenLicenseDenied_DoesNotCreateSmtpClient(string failureCode)
    {
        var client = new StubEmailSmtpClient();
        var factory = new StubEmailSmtpClientFactory(client);
        var service = CreateService(
            factory,
            new StubAuthorizer(LicenseAuthorizationResult.Denied(failureCode)));

        var result = await service.SendAsync(
            CreateOptions(),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(failureCode, result.ErrorCode);
        Assert.Equal(0, factory.CreateCount);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task TestConnectionAsync_DoesNotSendEmail()
    {
        var client = new StubEmailSmtpClient();
        var service = CreateService(
            client,
            new StubAuthorizer(LicenseAuthorizationResult.Authorized));

        var result = await service.TestConnectionAsync(
            CreateOptions(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(["connect", "authenticate", "disconnect"], client.Calls);
        Assert.Null(client.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_AuthorizerExceptionFailsClosed()
    {
        var factory = new StubEmailSmtpClientFactory(new StubEmailSmtpClient());
        var service = CreateService(factory, new ThrowingAuthorizer());

        var result = await service.TestConnectionAsync(
            CreateOptions(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task GetLicenseStatusAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(
            new StubEmailSmtpClient(),
            new StubAuthorizer(LicenseAuthorizationResult.Authorized));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetLicenseStatusAsync(cancellation.Token));
    }

    private static EmailNotificationTestService CreateService(
        StubEmailSmtpClient client,
        INotificationLicenseAuthorizer authorizer) => CreateService(
            new StubEmailSmtpClientFactory(client),
            authorizer);

    private static EmailNotificationTestService CreateService(
        IEmailSmtpClientFactory factory,
        INotificationLicenseAuthorizer authorizer)
    {
        var builder = new EmailMessageBuilder();
        return new EmailNotificationTestService(
            factory,
            new EmailTemplateProvider(
                Path.Combine(AppContext.BaseDirectory, "templates", "email-alarm.html"),
                builder),
            builder,
            authorizer);
    }

    private static EmailOptions CreateOptions() => new()
    {
        Enabled = true,
        Host = "smtp.example.invalid",
        Port = 587,
        SecurityMode = EmailSecurityMode.StartTls,
        AuthenticationMode = EmailAuthenticationMode.Password,
        Username = "smtp-user",
        Password = "desktop-test-password",
        SenderAddress = "alarm@example.invalid",
        SenderDisplayName = "AlarmSystem",
        Recipients = ["one@example.invalid", "two@example.invalid"],
        DestinationReference = "Operations",
    };

    private static AlarmNotification CreateNotification() => new(
        Guid.Parse("f25c6d39-6f7a-48a7-ac78-6e3b2fba8f4a"),
        "TEST.TAG",
        "Test area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "Desktop Email test",
        new DateTimeOffset(2026, 10, 12, 15, 0, 0, TimeSpan.FromHours(8)),
        "Test equipment");

    private sealed class StubAuthorizer(LicenseAuthorizationResult result)
        : INotificationLicenseAuthorizer
    {
        public NotificationFeature? LastFeature { get; private set; }

        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastFeature = feature;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingAuthorizer : INotificationLicenseAuthorizer
    {
        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sensitive SDK response must not escape.");
    }

    private sealed class StubEmailSmtpClientFactory(StubEmailSmtpClient client)
        : IEmailSmtpClientFactory
    {
        public int CreateCount { get; private set; }

        public IEmailSmtpClient Create()
        {
            CreateCount++;
            return client;
        }
    }

    private sealed class StubEmailSmtpClient : IEmailSmtpClient
    {
        public List<string> Calls { get; } = [];

        public MimeMessage? Message { get; private set; }

        public Task ConnectAsync(
            string host,
            int port,
            EmailSecurityMode securityMode,
            CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            Calls.Add("authenticate");
            return Task.CompletedTask;
        }

        public Task<string> SendAsync(
            MimeMessage message,
            CancellationToken cancellationToken)
        {
            Calls.Add("send");
            Message = message;
            return Task.FromResult("Accepted");
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
