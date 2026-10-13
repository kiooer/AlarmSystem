using System.ComponentModel;
using System.Net.Sockets;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
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

    [Theory]
    [InlineData(EmailSecurityMode.None, SecureSocketOptions.None)]
    [InlineData(EmailSecurityMode.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(EmailSecurityMode.SslTls, SecureSocketOptions.SslOnConnect)]
    public void SecurityModeMapper_UsesExplicitMailKitSocketOption(
        EmailSecurityMode securityMode,
        SecureSocketOptions expected)
    {
        Assert.Equal(
            expected,
            EmailSmtpSecurityModeMapper.ToSecureSocketOptions(securityMode));
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
        var logger = new RecordingLogger<EmailSmtpNotificationChannel>();
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = token => Task.FromCanceled(token),
        };
        var channel = CreateChannel(client, logger: logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(CreateNotification(), cancellation.Token));

        var log = Assert.Single(logger.Messages);
        Assert.Contains("CancellationRequested=True", log, StringComparison.Ordinal);
        Assert.Contains("TimeoutTriggered=False", log, StringComparison.Ordinal);
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
    public async Task SendAsync_WhenDnsReturnsNoIpv4_UsesExplicitRetryableError()
    {
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = _ => throw new EmailSmtpIpv4AddressNotFoundException(
                "smtp.example.invalid"),
        };
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, result.Status);
        Assert.Equal("email_smtp_ipv4_address_not_found", result.ErrorCode);
        Assert.Equal(["connect"], client.Calls);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_RecordsConnectFailureStageAndSocketCode()
    {
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        var failure = Assert.Single(
            execution.Diagnostics,
            diagnostic => diagnostic.Result == EmailSmtpStageResult.Failed);
        Assert.Equal(EmailSmtpStage.Connect, failure.Stage);
        Assert.Equal(EmailSmtpFailureKind.Transport, failure.FailureKind);
        Assert.Equal("email_smtp_transport_error", failure.ErrorCode);
        Assert.Equal(typeof(SocketException).FullName, failure.ExceptionType);
        Assert.Equal(SocketError.ConnectionRefused, failure.SocketErrorCode);
        Assert.NotNull(failure.NativeErrorCode);
        Assert.Equal(EmailSecurityMode.StartTls, failure.SecurityMode);
        Assert.Equal("smtp.example.invalid", failure.Host);
        Assert.Equal(587, failure.Port);
        Assert.Equal(nameof(SecureSocketOptions.StartTls), failure.SecureSocketOptions);
        Assert.False(failure.CancellationRequested);
        Assert.False(failure.TimeoutTriggered);
        Assert.NotEqual(Guid.Empty, failure.CorrelationId);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_RecordsSelectedIpv4Address()
    {
        var client = new StubEmailSmtpClient
        {
            RemoteAddress = "192.0.2.20",
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        var connect = Assert.Single(
            execution.Diagnostics,
            diagnostic => diagnostic.Stage == EmailSmtpStage.Connect);
        Assert.Equal("192.0.2.20", connect.Ipv4Address);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_FindsNestedSocketExceptionAndNativeCode()
    {
        var socketException = new SocketException((int)SocketError.ConnectionReset);
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = _ => throw new IOException("Connection failed.", socketException),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        var failure = execution.PrimaryFailure!;
        Assert.Equal(typeof(IOException).FullName, failure.ExceptionType);
        Assert.Equal(typeof(SocketException).FullName, failure.InnerExceptionType);
        Assert.Equal(SocketError.ConnectionReset, failure.SocketErrorCode);
        Assert.Equal(socketException.NativeErrorCode, failure.NativeErrorCode);
        Assert.Equal("email_smtp_transport_error", failure.ErrorCode);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_ClassifiesNestedSystemAuthenticationExceptionAsTls()
    {
        const int noCredentials = unchecked((int)0x8009030E);
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = _ => throw new IOException(
                "TLS failed.",
                new System.Security.Authentication.AuthenticationException(
                    "Authentication failed.",
                    new Win32Exception(noCredentials))),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal("email_smtp_tls_failed", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpFailureKind.Tls, execution.PrimaryFailure!.FailureKind);
        Assert.Equal(
            typeof(Win32Exception).FullName,
            execution.PrimaryFailure.InnerExceptionType);
        Assert.Equal(noCredentials, execution.PrimaryFailure.NativeErrorCode);
        Assert.Equal(
            typeof(System.Security.Authentication.AuthenticationException).FullName,
            execution.PrimaryFailure.AuthenticationExceptionType);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_RecordsTlsFailureAsConnectBecauseMailKitNegotiatesTlsInsideConnect()
    {
        var client = new StubEmailSmtpClient
        {
            ConnectHandler = _ => throw new SslHandshakeException("Handshake rejected."),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal("email_smtp_tls_failed", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpStage.Connect, execution.PrimaryFailure!.Stage);
        Assert.Equal(EmailSmtpFailureKind.Tls, execution.PrimaryFailure.FailureKind);
        Assert.Equal(EmailSecurityMode.StartTls, execution.PrimaryFailure.SecurityMode);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_RecordsAuthenticationFailureStage()
    {
        var client = new StubEmailSmtpClient
        {
            AuthenticateHandler = (_, _, _) => throw new AuthenticationException("Rejected."),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal("email_smtp_authentication_failed", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpStage.Authenticate, execution.PrimaryFailure!.Stage);
        Assert.Equal(EmailSmtpFailureKind.Authentication, execution.PrimaryFailure.FailureKind);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_RecordsAuthenticationTimeoutStage()
    {
        var client = new StubEmailSmtpClient
        {
            AuthenticateHandler = async (_, _, token) =>
                await Task.Delay(Timeout.InfiniteTimeSpan, token),
        };
        var channel = CreateChannel(
            client,
            CreateOptions(timeout: TimeSpan.FromMilliseconds(25)));

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.RetryableFailure, execution.Result.Status);
        Assert.Equal("email_timeout", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpStage.Authenticate, execution.PrimaryFailure!.Stage);
        Assert.Equal(EmailSmtpFailureKind.Timeout, execution.PrimaryFailure.FailureKind);
        Assert.True(execution.PrimaryFailure.TimeoutTriggered);
        Assert.False(execution.PrimaryFailure.CancellationRequested);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_WhenSendAndDisconnectSucceedRecordsBothStages()
    {
        var channel = CreateChannel(new StubEmailSmtpClient());

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, execution.Result.Status);
        Assert.Contains(
            execution.Diagnostics,
            diagnostic => diagnostic.Stage == EmailSmtpStage.Send &&
                          diagnostic.Result == EmailSmtpStageResult.Success);
        Assert.Contains(
            execution.Diagnostics,
            diagnostic => diagnostic.Stage == EmailSmtpStage.Disconnect &&
                          diagnostic.Result == EmailSmtpStageResult.Success);
        Assert.DoesNotContain(
            execution.Diagnostics,
            diagnostic => diagnostic.Result == EmailSmtpStageResult.Warning);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_WhenSendTransportBreaksMarksSubmissionUnknown()
    {
        var client = new StubEmailSmtpClient
        {
            SendHandler = (_, _) => throw new IOException("Connection closed."),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, execution.Result.Status);
        Assert.Equal("email_smtp_send_status_unknown", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpStage.Send, execution.PrimaryFailure!.Stage);
        Assert.Equal(
            EmailSmtpFailureKind.SubmissionStatusUnknown,
            execution.PrimaryFailure.FailureKind);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_WhenSendTimesOutMarksSubmissionUnknown()
    {
        var client = new StubEmailSmtpClient
        {
            SendHandler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "Unreachable";
            },
        };
        var channel = CreateChannel(
            client,
            CreateOptions(timeout: TimeSpan.FromMilliseconds(25)));

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, execution.Result.Status);
        Assert.Equal("email_smtp_send_status_unknown", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpStage.Send, execution.PrimaryFailure!.Stage);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_WhenDisconnectFailsKeepsSuccessfulSend()
    {
        var client = new StubEmailSmtpClient
        {
            DisconnectHandler = _ => throw new IOException("QUIT connection closed."),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, execution.Result.Status);
        Assert.Null(execution.PrimaryFailure);
        Assert.Equal("email_smtp_disconnect_warning", execution.Warning!.ErrorCode);
        Assert.Equal(EmailSmtpStage.Disconnect, execution.Warning.Stage);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_WhenDisconnectFailsStillReturnsSuccess()
    {
        var client = new StubEmailSmtpClient
        {
            DisconnectHandler = _ => throw new IOException("QUIT connection closed."),
        };
        var channel = CreateChannel(client);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, result.Status);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_WhenSendAndDisconnectFailPreservesSendFailure()
    {
        var client = new StubEmailSmtpClient
        {
            SendHandler = (_, _) => throw new SmtpCommandException(
                SmtpErrorCode.MessageNotAccepted,
                SmtpStatusCode.MailboxUnavailable,
                "Rejected."),
            DisconnectHandler = _ => throw new IOException("QUIT connection closed."),
        };
        var channel = CreateChannel(client);

        var execution = await channel.SendWithDiagnosticsAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, execution.Result.Status);
        Assert.Equal("email_smtp_command_error", execution.Result.ErrorCode);
        Assert.Equal(EmailSmtpStage.Send, execution.PrimaryFailure!.Stage);
        Assert.Equal("email_smtp_disconnect_warning", execution.Warning!.ErrorCode);
    }

    [Fact]
    public async Task TestConnectionWithDiagnosticsAsync_WhenDisconnectFailsKeepsSuccessfulTest()
    {
        var client = new StubEmailSmtpClient
        {
            DisconnectHandler = _ => throw new IOException("QUIT connection closed."),
        };
        var channel = CreateChannel(client);

        var execution = await channel.TestConnectionWithDiagnosticsAsync(
            CancellationToken.None);

        Assert.Equal(NotificationSendStatus.Succeeded, execution.Result.Status);
        Assert.Equal("email_smtp_disconnect_warning", execution.Warning!.ErrorCode);
    }

    [Fact]
    public async Task SendWithDiagnosticsAsync_LogsOnlyControlledFieldsWithoutSecrets()
    {
        const string password = "private-password-not-for-output";
        var logger = new RecordingLogger<EmailSmtpNotificationChannel>();
        var client = new StubEmailSmtpClient
        {
            AuthenticateHandler = (_, _, _) => throw new InvalidOperationException(password),
        };
        var channel = CreateChannel(client, CreateOptions(password: password), logger);

        await channel.SendWithDiagnosticsAsync(CreateNotification(), CancellationToken.None);

        var log = string.Join(Environment.NewLine, logger.Messages);
        Assert.DoesNotContain(password, log, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp-user", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Test message", log, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), log, StringComparison.Ordinal);
        Assert.Contains("Authenticate", log, StringComparison.Ordinal);
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
        EmailOptions? options = null,
        ILogger<EmailSmtpNotificationChannel>? logger = null) => CreateChannel(
            new StubEmailSmtpClientFactory(client),
            options ?? CreateOptions(),
            logger);

    private static EmailSmtpNotificationChannel CreateChannel(
        IEmailSmtpClientFactory factory,
        EmailOptions options,
        ILogger<EmailSmtpNotificationChannel>? logger = null)
    {
        var builder = new EmailMessageBuilder();
        return new EmailSmtpNotificationChannel(
            factory,
            new EmailTemplateProvider(Path.Combine(
                AppContext.BaseDirectory,
                "templates",
                "email-alarm.html"), builder),
            builder,
            options,
            logger);
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
        public string? RemoteAddress { get; init; }

        public Func<CancellationToken, Task> ConnectHandler { get; init; } =
            _ => Task.CompletedTask;

        public Func<string, string, CancellationToken, Task> AuthenticateHandler { get; init; } =
            (_, _, _) => Task.CompletedTask;

        public Func<MimeMessage, CancellationToken, Task<string>> SendHandler { get; init; } =
            (_, _) => Task.FromResult("Accepted");

        public Func<CancellationToken, Task> DisconnectHandler { get; init; } =
            _ => Task.CompletedTask;

        public List<string> Calls { get; } = [];

        public string? Host { get; private set; }

        public int? Port { get; private set; }

        public EmailSecurityMode? SecurityMode { get; private set; }

        public MimeMessage? Message { get; private set; }

        public bool IsDisposed { get; private set; }

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

        public async Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            Calls.Add("disconnect");
            await DisconnectHandler(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
