using System.Net.Sockets;
using AlarmSystem.Application.Abstractions.Notifications;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public static class EmailSmtpErrorClassifier
{
    public static NotificationSendResult Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            SmtpCommandException commandException => Classify(commandException),
            AuthenticationException => NotificationSendResult.Permanent(
                "email_smtp_authentication_failed"),
            SslHandshakeException or NotSupportedException => NotificationSendResult.Permanent(
                "email_smtp_tls_failed"),
            SocketException or IOException or SmtpProtocolException or
                ServiceNotConnectedException => NotificationSendResult.Retryable(
                    "email_smtp_transport_error"),
            ServiceNotAuthenticatedException => NotificationSendResult.Permanent(
                "email_smtp_authentication_failed"),
            _ => NotificationSendResult.Permanent("email_smtp_unexpected_error"),
        };
    }

    private static NotificationSendResult Classify(SmtpCommandException exception)
    {
        var statusCode = (int)exception.StatusCode;
        return statusCode is >= 400 and < 500
            ? NotificationSendResult.Retryable("email_smtp_command_error", statusCode)
            : NotificationSendResult.Permanent("email_smtp_command_error", statusCode);
    }
}
