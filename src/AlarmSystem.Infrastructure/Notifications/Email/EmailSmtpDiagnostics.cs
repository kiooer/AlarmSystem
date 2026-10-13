using System.Net.Sockets;
using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public enum EmailSmtpStage
{
    Validation = 0,
    Connect = 1,
    StartTls = 2,
    Authenticate = 3,
    Send = 4,
    Disconnect = 5,
}

public enum EmailSmtpStageResult
{
    Success = 1,
    Failed = 2,
    Warning = 3,
    Cancelled = 4,
}

public enum EmailSmtpFailureKind
{
    None = 0,
    Timeout = 1,
    Authentication = 2,
    Tls = 3,
    SmtpCommand = 4,
    Transport = 5,
    SubmissionStatusUnknown = 6,
    Cancelled = 7,
    Cleanup = 8,
    Unexpected = 9,
    Configuration = 10,
}

public sealed record EmailSmtpStageDiagnostic(
    Guid CorrelationId,
    EmailSmtpStage Stage,
    EmailSmtpStageResult Result,
    string? ErrorCode,
    string? ExceptionType,
    EmailSmtpFailureKind FailureKind,
    int? SmtpStatusCode,
    SocketError? SocketErrorCode,
    long DurationMilliseconds,
    EmailSecurityMode SecurityMode,
    string? InnerExceptionType = null,
    int? NativeErrorCode = null,
    string Host = "",
    int Port = 0,
    string SecureSocketOptions = "",
    bool CancellationRequested = false,
    bool TimeoutTriggered = false,
    string? AuthenticationExceptionType = null,
    string? Ipv4Address = null);

public sealed record EmailSmtpExecutionResult(
    NotificationSendResult Result,
    IReadOnlyList<EmailSmtpStageDiagnostic> Diagnostics)
{
    public EmailSmtpStageDiagnostic? PrimaryFailure => Diagnostics.FirstOrDefault(
        diagnostic => diagnostic.Result == EmailSmtpStageResult.Failed);

    public EmailSmtpStageDiagnostic? Warning => Diagnostics.FirstOrDefault(
        diagnostic => diagnostic.Result == EmailSmtpStageResult.Warning);

    public static EmailSmtpExecutionResult WithoutDiagnostics(NotificationSendResult result) =>
        new(result, []);
}

internal sealed record EmailSmtpFailureDetails(
    NotificationSendResult Result,
    EmailSmtpFailureKind FailureKind,
    string ExceptionType,
    string? InnerExceptionType,
    int? SmtpStatusCode,
    SocketError? SocketErrorCode,
    int? NativeErrorCode,
    string? AuthenticationExceptionType);
