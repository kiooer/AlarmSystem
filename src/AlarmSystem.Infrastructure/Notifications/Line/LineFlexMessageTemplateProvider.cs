using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Infrastructure.Notifications.Line;

public sealed class LineFlexMessageTemplateProvider
{
    private readonly string templatePath;
    private LineFlexMessageRenderer? renderer;

    public LineFlexMessageTemplateProvider(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        this.templatePath = templatePath;
        Reload();
    }

    public bool IsHealthy => renderer is not null;

    public string? FailureCode { get; private set; }

    public void Reload()
    {
        try
        {
            renderer = LineFlexMessageRenderer.LoadFromFile(templatePath);
            FailureCode = null;
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          InvalidOperationException or
                                          ArgumentException)
        {
            renderer = null;
            FailureCode = "line_template_unhealthy";
        }
    }

    public bool TryRender(AlarmNotification notification, out JsonObject? bubble)
    {
        var current = renderer;
        if (current is null)
        {
            bubble = null;
            return false;
        }

        bubble = current.Render(notification);
        return true;
    }
}
