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
    public void Constructor_DoesNotAcceptFormalLicenseAuthorizer()
    {
        var constructor = Assert.Single(typeof(EmailNotificationTestService).GetConstructors());

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(INotificationLicenseAuthorizer));
    }

    [Fact]
    public async Task SendAsync_UsesRawEmailTransportWithoutLicenseCheck()
    {
        var client = new StubEmailSmtpClient();
        var service = CreateService(client);

        var result = await service.SendAsync(
            CreateOptions(),
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Result.Status);
        Assert.Equal(["connect", "authenticate", "send", "disconnect"], client.Calls);
        Assert.Equal(2, client.Message!.To.Count);
    }

    [Fact]
    public async Task TestConnectionAsync_DoesNotSendEmail()
    {
        var client = new StubEmailSmtpClient();
        var service = CreateService(client);

        var result = await service.TestConnectionAsync(
            CreateConnectionOptions(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Result.Status);
        Assert.Equal(["connect", "authenticate", "disconnect"], client.Calls);
        Assert.Null(client.Message);
    }

    private static EmailOptions CreateConnectionOptions() => new()
    {
        Enabled = true,
        Host = "smtp.example.invalid",
        Port = 587,
        SecurityMode = EmailSecurityMode.StartTls,
        AuthenticationMode = EmailAuthenticationMode.Password,
        Username = "smtp-user",
        Password = "desktop-test-password",
    };

    private static EmailNotificationTestService CreateService(
        StubEmailSmtpClient client) => CreateService(new StubEmailSmtpClientFactory(client));

    private static EmailNotificationTestService CreateService(
        IEmailSmtpClientFactory factory)
    {
        var builder = new EmailMessageBuilder();
        return new EmailNotificationTestService(
            factory,
            new EmailTemplateProvider(
                Path.Combine(AppContext.BaseDirectory, "templates", "email-alarm.html"),
                builder),
            builder);
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
