using System.Diagnostics;
using System.Net.Sockets;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed class EmailSmtpNotificationChannel : INotificationChannel
{
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly EventId SmtpStageEventId = new(3001, "EmailSmtpStage");

    private readonly IEmailSmtpClientFactory clientFactory;
    private readonly EmailTemplateProvider templateProvider;
    private readonly EmailMessageBuilder messageBuilder;
    private readonly EmailOptions options;
    private readonly ILogger<EmailSmtpNotificationChannel> logger;

    public EmailSmtpNotificationChannel(
        IEmailSmtpClientFactory clientFactory,
        EmailTemplateProvider templateProvider,
        EmailMessageBuilder messageBuilder,
        EmailOptions options,
        ILogger<EmailSmtpNotificationChannel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(templateProvider);
        ArgumentNullException.ThrowIfNull(messageBuilder);
        ArgumentNullException.ThrowIfNull(options);

        this.clientFactory = clientFactory;
        this.templateProvider = templateProvider;
        this.messageBuilder = messageBuilder;
        this.options = options;
        this.logger = logger ?? NullLogger<EmailSmtpNotificationChannel>.Instance;
    }

    public string ChannelName => "Email";

    public string DestinationReference => options.DestinationReference;

    public async Task<NotificationSendResult> SendAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        var execution = await SendWithDiagnosticsAsync(notification, cancellationToken);
        return execution.Result;
    }

    public async Task<EmailSmtpExecutionResult> SendWithDiagnosticsAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            options.Validate();
        }
        catch (InvalidOperationException)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("email_configuration_invalid"));
        }

        if (!options.Enabled)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("channel_disabled"));
        }

        if (notification.EventType == AlarmEventType.Ack)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("email_ack_not_notified"));
        }

        if (notification.EventType != AlarmEventType.Alarm)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("email_event_not_supported"));
        }

        MimeMessage message;
        try
        {
            if (!templateProvider.TryRender(notification, out var content))
            {
                return EmailSmtpExecutionResult.WithoutDiagnostics(
                    NotificationSendResult.Permanent(
                        templateProvider.FailureCode ?? "email_template_unhealthy"));
            }

            message = messageBuilder.BuildMessage(content!, options);
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          InvalidOperationException or
                                          FormatException)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("email_message_invalid"));
        }

        return await ExecuteSmtpAsync(message, cancellationToken);
    }

    public async Task<NotificationSendResult> TestConnectionAsync(
        CancellationToken cancellationToken)
    {
        var execution = await TestConnectionWithDiagnosticsAsync(cancellationToken);
        return execution.Result;
    }

    public async Task<EmailSmtpExecutionResult> TestConnectionWithDiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            options.ValidateSmtpConnection();
        }
        catch (InvalidOperationException)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("email_configuration_invalid"));
        }

        if (!options.Enabled)
        {
            return EmailSmtpExecutionResult.WithoutDiagnostics(
                NotificationSendResult.Permanent("channel_disabled"));
        }

        return await ExecuteSmtpAsync(message: null, cancellationToken);
    }

    private async Task<EmailSmtpExecutionResult> ExecuteSmtpAsync(
        MimeMessage? message,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var diagnostics = new List<EmailSmtpStageDiagnostic>();
        IEmailSmtpClient? client = null;
        var connected = false;
        NotificationSendResult? result = null;

        try
        {
            result = await ExecutePrimaryStageAsync(
                EmailSmtpStage.Connect,
                async token =>
                {
                    client = clientFactory.Create();
                    await client.ConnectAsync(
                        options.Host,
                        options.Port,
                        options.SecurityMode,
                        token);
                },
                correlationId,
                diagnostics,
                cancellationToken,
                () => client?.RemoteAddress);

            if (result is null)
            {
                connected = true;
            }

            if (result is null &&
                options.AuthenticationMode == EmailAuthenticationMode.Password)
            {
                result = await ExecutePrimaryStageAsync(
                    EmailSmtpStage.Authenticate,
                    token => client!.AuthenticateAsync(
                        options.Username,
                        options.Password,
                        token),
                    correlationId,
                    diagnostics,
                    cancellationToken,
                    () => client?.RemoteAddress);
            }

            if (result is null && message is not null)
            {
                result = await ExecutePrimaryStageAsync(
                    EmailSmtpStage.Send,
                    async token =>
                    {
                        await client!.SendAsync(message, token);
                    },
                    correlationId,
                    diagnostics,
                    cancellationToken,
                    () => client?.RemoteAddress);
            }

            result ??= NotificationSendResult.Success();
        }
        finally
        {
            if (client is not null)
            {
                if (connected)
                {
                    await TryDisconnectAsync(client, correlationId, diagnostics);
                }

                await TryDisposeAsync(client, correlationId, diagnostics);
            }
        }

        return new EmailSmtpExecutionResult(result, diagnostics.ToArray());
    }

    private async Task<NotificationSendResult?> ExecutePrimaryStageAsync(
        EmailSmtpStage stage,
        Func<CancellationToken, Task> action,
        Guid correlationId,
        List<EmailSmtpStageDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        Func<string?>? ipv4AddressProvider = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var started = Stopwatch.GetTimestamp();

        try
        {
            await action(timeout.Token);
            RecordDiagnostic(
                diagnostics,
                CreateDiagnostic(
                    correlationId,
                    stage,
                    EmailSmtpStageResult.Success,
                    started,
                    ipv4Address: ipv4AddressProvider?.Invoke()));
            return null;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            RecordDiagnostic(
                diagnostics,
                CreateDiagnostic(
                    correlationId,
                    stage,
                    EmailSmtpStageResult.Cancelled,
                    started,
                    "email_operation_cancelled",
                    GetExceptionType(exception),
                    EmailSmtpFailureKind.Cancelled,
                    cancellationRequested: true,
                    ipv4Address: ipv4AddressProvider?.Invoke()));
            throw;
        }
        catch (OperationCanceledException exception)
        {
            var result = stage == EmailSmtpStage.Send
                ? NotificationSendResult.Permanent("email_smtp_send_status_unknown")
                : NotificationSendResult.Retryable("email_timeout");
            var failureKind = stage == EmailSmtpStage.Send
                ? EmailSmtpFailureKind.SubmissionStatusUnknown
                : EmailSmtpFailureKind.Timeout;

            RecordDiagnostic(
                diagnostics,
                CreateDiagnostic(
                    correlationId,
                    stage,
                    EmailSmtpStageResult.Failed,
                    started,
                    result.ErrorCode,
                    GetExceptionType(exception),
                    failureKind,
                    timeoutTriggered: true,
                    ipv4Address: ipv4AddressProvider?.Invoke()));
            return result;
        }
        catch (Exception exception)
        {
            var failure = EmailSmtpErrorClassifier.Describe(exception, stage);
            RecordDiagnostic(
                diagnostics,
                CreateDiagnostic(
                    correlationId,
                    stage,
                    EmailSmtpStageResult.Failed,
                    started,
                    failure.Result.ErrorCode,
                    failure.ExceptionType,
                    failure.FailureKind,
                    failure.SmtpStatusCode,
                    failure.SocketErrorCode,
                    failure.InnerExceptionType,
                    failure.NativeErrorCode,
                    cancellationRequested: cancellationToken.IsCancellationRequested,
                    authenticationExceptionType: failure.AuthenticationExceptionType,
                    ipv4Address: ipv4AddressProvider?.Invoke()));
            return failure.Result;
        }
    }

    private async Task TryDisconnectAsync(
        IEmailSmtpClient client,
        Guid correlationId,
        List<EmailSmtpStageDiagnostic> diagnostics)
    {
        using var cleanupTimeout = new CancellationTokenSource(DisconnectTimeout);
        var started = Stopwatch.GetTimestamp();

        try
        {
            await client.DisconnectAsync(quit: true, cleanupTimeout.Token);
            RecordDiagnostic(
                diagnostics,
                CreateDiagnostic(
                    correlationId,
                    EmailSmtpStage.Disconnect,
                    EmailSmtpStageResult.Success,
                    started,
                    ipv4Address: client.RemoteAddress));
        }
        catch (OperationCanceledException exception)
        {
            RecordCleanupWarning(
                diagnostics,
                correlationId,
                started,
                exception,
                EmailSmtpFailureKind.Timeout,
                timeoutTriggered: cleanupTimeout.IsCancellationRequested,
                ipv4Address: client.RemoteAddress);
        }
        catch (Exception exception)
        {
            var failure = EmailSmtpErrorClassifier.Describe(
                exception,
                EmailSmtpStage.Disconnect);
            RecordCleanupWarning(
                diagnostics,
                correlationId,
                started,
                exception,
                failure.FailureKind,
                failure.SmtpStatusCode,
                failure.SocketErrorCode,
                failure.InnerExceptionType,
                failure.NativeErrorCode,
                authenticationExceptionType: failure.AuthenticationExceptionType,
                ipv4Address: client.RemoteAddress);
        }
    }

    private async Task TryDisposeAsync(
        IEmailSmtpClient client,
        Guid correlationId,
        List<EmailSmtpStageDiagnostic> diagnostics)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception exception)
        {
            var failure = EmailSmtpErrorClassifier.Describe(
                exception,
                EmailSmtpStage.Disconnect);
            RecordCleanupWarning(
                diagnostics,
                correlationId,
                started,
                exception,
                EmailSmtpFailureKind.Cleanup,
                failure.SmtpStatusCode,
                failure.SocketErrorCode,
                failure.InnerExceptionType,
                failure.NativeErrorCode,
                authenticationExceptionType: failure.AuthenticationExceptionType,
                ipv4Address: client.RemoteAddress);
        }
    }

    private void RecordCleanupWarning(
        List<EmailSmtpStageDiagnostic> diagnostics,
        Guid correlationId,
        long started,
        Exception exception,
        EmailSmtpFailureKind failureKind,
        int? smtpStatusCode = null,
        SocketError? socketErrorCode = null,
        string? innerExceptionType = null,
        int? nativeErrorCode = null,
        bool timeoutTriggered = false,
        string? authenticationExceptionType = null,
        string? ipv4Address = null)
    {
        RecordDiagnostic(
            diagnostics,
            CreateDiagnostic(
                correlationId,
                EmailSmtpStage.Disconnect,
                EmailSmtpStageResult.Warning,
                started,
                "email_smtp_disconnect_warning",
                GetExceptionType(exception),
                failureKind,
                smtpStatusCode,
                socketErrorCode,
                innerExceptionType,
                nativeErrorCode,
                timeoutTriggered: timeoutTriggered,
                authenticationExceptionType: authenticationExceptionType,
                ipv4Address: ipv4Address));
    }

    private EmailSmtpStageDiagnostic CreateDiagnostic(
        Guid correlationId,
        EmailSmtpStage stage,
        EmailSmtpStageResult result,
        long started,
        string? errorCode = null,
        string? exceptionType = null,
        EmailSmtpFailureKind failureKind = EmailSmtpFailureKind.None,
        int? smtpStatusCode = null,
        SocketError? socketErrorCode = null,
        string? innerExceptionType = null,
        int? nativeErrorCode = null,
        bool cancellationRequested = false,
        bool timeoutTriggered = false,
        string? authenticationExceptionType = null,
        string? ipv4Address = null) => new(
            correlationId,
            stage,
            result,
            errorCode,
            exceptionType,
            failureKind,
            smtpStatusCode,
            socketErrorCode,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            options.SecurityMode,
            innerExceptionType,
            nativeErrorCode,
            options.Host,
            options.Port,
            EmailSmtpSecurityModeMapper.ToSecureSocketOptions(options.SecurityMode).ToString(),
            cancellationRequested,
            timeoutTriggered,
            authenticationExceptionType,
            ipv4Address);

    private void RecordDiagnostic(
        List<EmailSmtpStageDiagnostic> diagnostics,
        EmailSmtpStageDiagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
        var level = diagnostic.Result switch
        {
            EmailSmtpStageResult.Failed => LogLevel.Error,
            EmailSmtpStageResult.Warning => LogLevel.Warning,
            EmailSmtpStageResult.Cancelled => LogLevel.Information,
            _ => LogLevel.Debug,
        };

        logger.Log(
            level,
            SmtpStageEventId,
            "Email SMTP stage completed. CorrelationId={CorrelationId} Stage={SmtpStage} Result={StageResult} ErrorCode={ErrorCode} ExceptionType={ExceptionType} InnerExceptionType={InnerExceptionType} AuthenticationExceptionType={AuthenticationExceptionType} FailureKind={FailureKind} SmtpStatusCode={SmtpStatusCode} SocketErrorCode={SocketErrorCode} NativeErrorCode={NativeErrorCode} Host={SmtpHost} Ipv4Address={SmtpIpv4Address} Port={SmtpPort} SecureSocketOptions={SecureSocketOptions} DurationMs={DurationMs} CancellationRequested={CancellationRequested} TimeoutTriggered={TimeoutTriggered}",
            diagnostic.CorrelationId,
            diagnostic.Stage,
            diagnostic.Result,
            diagnostic.ErrorCode,
            diagnostic.ExceptionType,
            diagnostic.InnerExceptionType,
            diagnostic.AuthenticationExceptionType,
            diagnostic.FailureKind,
            diagnostic.SmtpStatusCode,
            diagnostic.SocketErrorCode,
            diagnostic.NativeErrorCode,
            diagnostic.Host,
            diagnostic.Ipv4Address,
            diagnostic.Port,
            diagnostic.SecureSocketOptions,
            diagnostic.DurationMilliseconds,
            diagnostic.CancellationRequested,
            diagnostic.TimeoutTriggered);
    }

    private static string GetExceptionType(Exception exception) =>
        exception.GetType().FullName ?? exception.GetType().Name;
}
