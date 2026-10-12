using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed class EmailTemplateProvider
{
    private readonly string templatePath;
    private readonly EmailMessageBuilder messageBuilder;
    private string? template;

    public EmailTemplateProvider(string templatePath, EmailMessageBuilder messageBuilder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentNullException.ThrowIfNull(messageBuilder);
        this.templatePath = templatePath;
        this.messageBuilder = messageBuilder;
        Reload();
    }

    public bool IsHealthy => template is not null;

    public string? FailureCode { get; private set; }

    public void Reload()
    {
        try
        {
            var candidate = File.ReadAllText(templatePath);
            EmailMessageBuilder.ValidateTemplate(candidate);
            template = candidate;
            FailureCode = null;
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          InvalidOperationException or
                                          ArgumentException)
        {
            template = null;
            FailureCode = "email_template_unhealthy";
        }
    }

    public bool TryRender(
        AlarmNotification notification,
        out EmailMessageContent? content)
    {
        var current = template;
        if (current is null)
        {
            content = null;
            return false;
        }

        content = messageBuilder.Render(current, notification);
        return true;
    }
}
