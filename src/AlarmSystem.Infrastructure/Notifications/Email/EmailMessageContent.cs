namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed record EmailMessageContent(
    string Subject,
    string HtmlBody,
    string PlainTextBody);
