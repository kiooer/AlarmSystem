using System.Net.Sockets;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AlarmSystem.IntegrationTests.Notifications.Email;

public sealed class EmailSmtpNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_ConnectsAuthenticatesAndSendsMimeMessage()
    {
        var client = new StubEmailSmtpClient();
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(["connect", "authenticate", "send", "disconnect"], client.Calls);
        Assert.Equal("smtp.example.invalid", client.Host);
        Assert.Equal(587, client.Port);
        Assert.Equal(EmailSecurityMode.StartTls, client.SecurityMode);
        Assert.NotNull(client.Message);
        Assert.Equal(2, client.Message!.To.Count);
        Assert.NotNull(client.Message.HtmlBody);
        Assert.NotNull(client.Message.TextBody);
    }

    [Theory]
    [InlineData(EmailSecurityMode.StartTls)]
    [InlineData(EmailSecurityMode.SslTls)]
    public async Task SendAsync_UsesConfiguredEncryptedSecurityMode(
        EmailSecurityMode securityMode)
    {
        var client = new StubEmailSmtpClient();
        var channel = CreateChannel(client, CreateOptions(securityMode: securityMode));

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(securityMode, client.SecurityMode);
        Assert.Contains("authenticate", client.Calls);
    }

    [Fact]
    public async Task SendAsync_SkipsAuthenticationForInternalRelay()
    {
        var client = new StubEmailSmtpClient();
        var channel = CreateChannel(client, CreateRelayOptions());

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(["connect", "send", "disconnect"], client.Calls);
        Assert.Equal(EmailSecurityMode.None, client.SecurityMode);
    }

    [Fact]
    public async Task SendAsync_DoesNotCreateClientWhenDisabled()
    {
        var client = new StubEmailSmtpClient();
        var options = new EmailOptions { Enabled = false };
        var factory = new StubEmailSmtpClientFactory(client);
        var channel = CreateChannel(factory, options);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("channel_disabled", result.ErrorCode);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task SendAsync_DoesNotRenderOrConnectForAck()
    {
        var client = new StubEmailSmtpClient();
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(
            CreateNotification(eventType: AlarmEventType.Ack),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("email_ack_not_notified", result.ErrorCode);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task SendAsync_DoesNotConnectWhenTemplateIsUnhealthy()
    {
        var client = new StubEmailSmtpClient();
        var options = CreateOptions();
        var provider = new EmailTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "missing-email-template.html"),
            new EmailMessageBuilder());
        var channel = new EmailSmtpNotificationChannel(
            new StubEmailSmtpClientFactory(client),
            provider,
            new EmailMessageBuilder(),
            options);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("email_template_unhealthy", result.ErrorCode);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task TestConnectionAsync_DoesNotSendMessage()
    {
        var client = new StubEmailSmtpClient();
        var channel = CreateChannel(client);

        var result = await channel.TestConnectionAsync(CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(["connect", "authenticate", "disconnect"], client.Calls);
        Assert.Null(client.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_DoesNotRequireMessageAddressesOrDestination()
    {
        var client = new StubEmailSmtpClient();
        var options = new EmailOptions
        {
            Enabled = true,
            Host = "smtp.example.invalid",
            Port = 587,
            SecurityMode = EmailSecurityMode.StartTls,
            AuthenticationMode = EmailAuthenticationMode.Password,
            Username = "smtp-user",
            Password = "integration-test-password",
        };
        var channel = CreateChannel(client, options);

        var result = await channel.TestConnectionAsync(CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Equal(["connect", "authenticate", "disconnect"], client.Calls);
    }

    [Fact]
    public async Task SendAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = token => Task.FromCanceled(token),
        };
        var channel = CreateChannel(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(CreateNotification(), cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_ClassifiesOperationTimeoutAsRetryable()
    {
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = async token => await Task.Delay(Timeout.InfiniteTimeSpan, token),
        };
        var options = CreateOptions(timeout: TimeSpan.FromMilliseconds(25));
        var channel = CreateChannel(client, options);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("email_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesAuthenticationFailureAsPermanent()
    {
        var client = new StubEmailSmtpClient
        {
            AuthenticateHandler = (_, _, _) => throw new AuthenticationException("Rejected."),
        };
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("email_smtp_authentication_failed", result.ErrorCode);
    }

    [Theory]
    [InlineData(SmtpStatusCode.MailboxBusy, NotificationSendStatus.RetryableFailure)]
    [InlineData(SmtpStatusCode.MailboxUnavailable, NotificationSendStatus.PermanentFailure)]
    public async Task SendAsync_ClassifiesSmtpStatusCode(
        SmtpStatusCode smtpStatusCode,
        NotificationSendStatus expectedStatus)
    {
        var client = new StubEmailSmtpClient
        {
            SendHandler = (_, _) => throw new SmtpCommandException(
                SmtpErrorCode.MessageNotAccepted,
                smtpStatusCode,
                "Rejected."),
        };
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("email_smtp_command_error", result.ErrorCode);
        Assert.Equal((int)smtpStatusCode, result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_ClassifiesSocketFailureAsRetryable()
    {
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("email_smtp_transport_error", result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_DoesNotExposePasswordInFailureResult()
    {
        const string password = "private-password-not-for-output";
        var client = new StubEmailSmtpClient
        {
            AuthenticateHandler = (_, _, _) => throw new InvalidOperationException(password),
        };
        var channel = CreateChannel(client, CreateOptions(password: password));

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal("email_smtp_unexpected_error", result.ErrorCode);
        Assert.DoesNotContain(password, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_UsesEmailNameAndConfiguredGroupReference()
    {
        var channel = CreateChannel(new StubEmailSmtpClient());

        Assert.Equal("Email", channel.ChannelName);
        Assert.Equal("Operations", channel.DestinationReference);
    }

    private static EmailSmtpNotificationChannel CreateChannel(
        StubEmailSmtpClient client,
        EmailOptions? options = null) => CreateChannel(
            new StubEmailSmtpClientFactory(client),
            options ?? CreateOptions());

    private static EmailSmtpNotificationChannel CreateChannel(
        IEmailSmtpClientFactory factory,
        EmailOptions options)
    {
        var builder = new EmailMessageBuilder();
        return new EmailSmtpNotificationChannel(
            factory,
            new EmailTemplateProvider(Path.Combine(
                AppContext.BaseDirectory,
                "templates",
                "email-alarm.html"), builder),
            builder,
            options);
    }

    private static EmailOptions CreateOptions(
        TimeSpan? timeout = null,
        string password = "unit-test-password",
        EmailSecurityMode securityMode = EmailSecurityMode.StartTls) => new()
        {
            Enabled = true,
            Host = "smtp.example.invalid",
            Port = 587,
            SecurityMode = securityMode,
            AuthenticationMode = EmailAuthenticationMode.Password,
            Username = "smtp-user",
            Password = password,
            SenderAddress = "alarm@example.invalid",
            SenderDisplayName = "AlarmSystem",
            Recipients = ["one@example.invalid", "two@example.invalid"],
            DestinationReference = "Operations",
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };

    private static EmailOptions CreateRelayOptions() => new()
    {
        Enabled = true,
        Host = "relay.internal.invalid",
        Port = 25,
        SecurityMode = EmailSecurityMode.None,
        AllowUnencryptedInternalRelay = true,
        AuthenticationMode = EmailAuthenticationMode.None,
        SenderAddress = "alarm@example.invalid",
        Recipients = ["operator@example.invalid"],
        DestinationReference = "InternalRelay",
    };

    private static AlarmNotification CreateNotification(
        AlarmEventType eventType = AlarmEventType.Alarm) => new(
            Guid.Parse("1f22543d-fde7-467a-b388-05344d6ef2ad"),
            "TEST.TAG",
            "Test area",
            eventType,
            AlarmSeverity.High,
            "Test message",
            new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.FromHours(8)),
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
        public Func<CancellationToken, Task> ConnectHandler { get; init; } =
            _ => Task.CompletedTask;

        public Func<string, string, CancellationToken, Task> AuthenticateHandler { get; init; } =
            (_, _, _) => Task.CompletedTask;

        public Func<MimeMessage, CancellationToken, Task<string>> SendHandler { get; init; } =
            (_, _) => Task.FromResult("Accepted");

        public List<string> Calls { get; } = [];

        public string? Host { get; private set; }

        public int? Port { get; private set; }

        public EmailSecurityMode? SecurityMode { get; private set; }

        public MimeMessage? Message { get; private set; }

        public async Task ConnectAsync(
            string host,
            int port,
            EmailSecurityMode securityMode,
            CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            Host = host;
            Port = port;
            SecurityMode = securityMode;
            await ConnectHandler(cancellationToken);
        }

        public async Task AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            Calls.Add("authenticate");
            await AuthenticateHandler(username, password, cancellationToken);
        }

        public async Task<string> SendAsync(
            MimeMessage message,
            CancellationToken cancellationToken)
        {
            Calls.Add("send");
            Message = message;
            return await SendHandler(message, cancellationToken);
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
