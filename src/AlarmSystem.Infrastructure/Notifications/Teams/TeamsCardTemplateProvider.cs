using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Infrastructure.Notifications.Teams;

public sealed class TeamsCardTemplateProvider
{
    private readonly string templatePath;
    private TeamsAdaptiveCardRenderer? renderer;

    public TeamsCardTemplateProvider(string templatePath)
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
            var candidate = TeamsAdaptiveCardRenderer.LoadFromFile(templatePath);
            renderer = candidate;
            FailureCode = null;
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          InvalidOperationException or
                                          ArgumentException)
        {
            renderer = null;
            FailureCode = "teams_template_unhealthy";
        }
    }

    public bool TryRender(AlarmNotification notification, out JsonObject? card)
    {
        var current = renderer;
        if (current is null)
        {
            card = null;
            return false;
        }

        card = current.Render(notification);
        return true;
    }
}
