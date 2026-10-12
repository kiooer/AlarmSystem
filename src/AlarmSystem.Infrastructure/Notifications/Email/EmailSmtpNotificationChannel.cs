using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed class EmailSmtpNotificationChannel : INotificationChannel
{
    private readonly IEmailSmtpClientFactory clientFactory;
    private readonly EmailTemplateProvider templateProvider;
    private readonly EmailMessageBuilder messageBuilder;
    private readonly EmailOptions options;

    public EmailSmtpNotificationChannel(
        IEmailSmtpClientFactory clientFactory,
        EmailTemplateProvider templateProvider,
        EmailMessageBuilder messageBuilder,
        EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(templateProvider);
        ArgumentNullException.ThrowIfNull(messageBuilder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        this.clientFactory = clientFactory;
        this.templateProvider = templateProvider;
        this.messageBuilder = messageBuilder;
        this.options = options;
    }

    public string ChannelName => "Email";

    public string DestinationReference => options.DestinationReference;

    public async Task<NotificationSendResult> SendAsync(
        AlarmNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!options.Enabled)
        {
            return NotificationSendResult.Permanent("channel_disabled");
        }

        if (notification.EventType == AlarmEventType.Ack)
        {
            return NotificationSendResult.Permanent("email_ack_not_notified");
        }

        if (notification.EventType != AlarmEventType.Alarm)
        {
            return NotificationSendResult.Permanent("email_event_not_supported");
        }

        MimeMessage message;
        try
        {
            if (!templateProvider.TryRender(notification, out var content))
            {
                return NotificationSendResult.Permanent(
                    templateProvider.FailureCode ?? "email_template_unhealthy");
            }

            message = messageBuilder.BuildMessage(content!, options);
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          InvalidOperationException or
                                          FormatException)
        {
            return NotificationSendResult.Permanent("email_message_invalid");
        }

        return await ExecuteSmtpAsync(message, cancellationToken);
    }

    public async Task<NotificationSendResult> TestConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return NotificationSendResult.Permanent("channel_disabled");
        }

        return await ExecuteSmtpAsync(message: null, cancellationToken);
    }

    private async Task<NotificationSendResult> ExecuteSmtpAsync(
        MimeMessage? message,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var operationToken = timeout.Token;

        try
        {
            await using var client = clientFactory.Create();
            await client.ConnectAsync(
                options.Host,
                options.Port,
                options.SecurityMode,
                operationToken);
            if (options.AuthenticationMode == EmailAuthenticationMode.Password)
            {
                await client.AuthenticateAsync(
                    options.Username,
                    options.Password,
                    operationToken);
            }

            if (message is not null)
            {
                await client.SendAsync(message, operationToken);
            }

            await client.DisconnectAsync(quit: true, operationToken);
            return NotificationSendResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return NotificationSendResult.Retryable("email_timeout");
        }
        catch (Exception exception)
        {
            return EmailSmtpErrorClassifier.Classify(exception);
        }
    }
}
