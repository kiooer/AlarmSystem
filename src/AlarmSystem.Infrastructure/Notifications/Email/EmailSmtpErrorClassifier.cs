using System.ComponentModel;
using System.Net.Sockets;
using AlarmSystem.Application.Abstractions.Notifications;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailKitAuthenticationException = MailKit.Security.AuthenticationException;
using SystemAuthenticationException = System.Security.Authentication.AuthenticationException;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public static class EmailSmtpErrorClassifier
{
    public static NotificationSendResult Classify(Exception exception)
    {
        return Describe(exception, EmailSmtpStage.Connect).Result;
    }

    internal static EmailSmtpFailureDetails Describe(
        Exception exception,
        EmailSmtpStage stage)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var commandException = FindException<SmtpCommandException>(exception);
        var ipv4AddressNotFoundException =
            FindException<EmailSmtpIpv4AddressNotFoundException>(exception);
        var systemAuthenticationException =
            FindException<SystemAuthenticationException>(exception);
        var mailKitAuthenticationException =
            FindException<MailKitAuthenticationException>(exception);
        var isTlsFailure = FindException<SslHandshakeException>(exception) is not null ||
                           systemAuthenticationException is not null ||
                           FindException<NotSupportedException>(exception) is not null;
        var isAuthenticationFailure = mailKitAuthenticationException is not null ||
            FindException<ServiceNotAuthenticatedException>(exception) is not null;
        var isTransportFailure = FindException<SocketException>(exception) is not null ||
                                 FindException<IOException>(exception) is not null ||
                                 FindException<SmtpProtocolException>(exception) is not null ||
                                 FindException<ServiceNotConnectedException>(exception) is not null;

        NotificationSendResult result;
        EmailSmtpFailureKind failureKind;
        if (ipv4AddressNotFoundException is not null)
        {
            result = NotificationSendResult.Retryable(
                "email_smtp_ipv4_address_not_found");
            failureKind = EmailSmtpFailureKind.Transport;
        }
        else if (commandException is not null)
        {
            result = Classify(commandException);
            failureKind = EmailSmtpFailureKind.SmtpCommand;
        }
        else if (isTlsFailure)
        {
            result = NotificationSendResult.Permanent("email_smtp_tls_failed");
            failureKind = EmailSmtpFailureKind.Tls;
        }
        else if (isAuthenticationFailure)
        {
            result = NotificationSendResult.Permanent("email_smtp_authentication_failed");
            failureKind = EmailSmtpFailureKind.Authentication;
        }
        else if (isTransportFailure)
        {
            result = NotificationSendResult.Retryable("email_smtp_transport_error");
            failureKind = EmailSmtpFailureKind.Transport;
        }
        else
        {
            result = NotificationSendResult.Permanent("email_smtp_unexpected_error");
            failureKind = EmailSmtpFailureKind.Unexpected;
        }

        if (stage == EmailSmtpStage.Send && isTransportFailure && !isTlsFailure)
        {
            result = NotificationSendResult.Permanent("email_smtp_send_status_unknown");
            failureKind = EmailSmtpFailureKind.SubmissionStatusUnknown;
        }

        var socketException = FindException<SocketException>(exception);
        var win32Exception = FindException<Win32Exception>(exception);
        var deepestInnerException = FindDeepestInnerException(exception);
        return new EmailSmtpFailureDetails(
            result,
            failureKind,
            GetExceptionType(exception),
            deepestInnerException is null ? null : GetExceptionType(deepestInnerException),
            commandException is null ? null : (int)commandException.StatusCode,
            socketException?.SocketErrorCode,
            socketException?.NativeErrorCode ?? win32Exception?.NativeErrorCode,
            systemAuthenticationException is not null
                ? GetExceptionType(systemAuthenticationException)
                : mailKitAuthenticationException is null
                    ? null
                    : GetExceptionType(mailKitAuthenticationException));
    }

    private static NotificationSendResult Classify(SmtpCommandException exception)
    {
        var statusCode = (int)exception.StatusCode;
        return statusCode is >= 400 and < 500
            ? NotificationSendResult.Retryable("email_smtp_command_error", statusCode)
            : NotificationSendResult.Permanent("email_smtp_command_error", statusCode);
    }

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private static Exception? FindDeepestInnerException(Exception exception)
    {
        if (exception.InnerException is null)
        {
            return null;
        }

        var current = exception.InnerException;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }

    private static string GetExceptionType(Exception exception) =>
        exception.GetType().FullName ?? exception.GetType().Name;
}
