namespace AlarmSystem.Application.Abstractions.Notifications;

public enum NotificationSendStatus
{
    Succeeded = 1,
    RetryableFailure = 2,
    PermanentFailure = 3,
}

public sealed record NotificationSendResult(
    NotificationSendStatus Status,
    string? ErrorCode = null,
    int? HttpStatusCode = null,
    string? ExternalMessageId = null)
{
    public static NotificationSendResult Success(string? externalMessageId = null) =>
        new(NotificationSendStatus.Succeeded, ExternalMessageId: externalMessageId);

    public static NotificationSendResult Retryable(string errorCode, int? httpStatusCode = null) =>
        new(NotificationSendStatus.RetryableFailure, errorCode, httpStatusCode);

    public static NotificationSendResult Permanent(string errorCode, int? httpStatusCode = null) =>
        new(NotificationSendStatus.PermanentFailure, errorCode, httpStatusCode);
}
