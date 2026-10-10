using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.IntegrationTests.Notifications.Teams;

public sealed class TeamsAdaptiveCardRendererTests
{
    [Theory]
    [InlineData(AlarmSeverity.High, "Attention")]
    [InlineData(AlarmSeverity.Medium, "Warning")]
    [InlineData(AlarmSeverity.Low, "Good")]
    public void Render_UsesExpectedColorAndSafelyPreservesText(
        AlarmSeverity severity,
        string expectedColor)
    {
        var renderer = CreateRenderer();
        var notification = new AlarmNotification(
            Guid.Parse("8b7d749e-04db-41fd-a3a0-21bf9a146b63"),
            "溫度,Tag\"A",
            null,
            AlarmEventType.Alarm,
            severity,
            "第一行\n第二行 \"quoted\" {{literal}}",
            new DateTimeOffset(2026, 10, 10, 14, 30, 15, TimeSpan.FromHours(8)));

        var card = renderer.Render(notification);
        var body = card["body"]!.AsArray();
        var facts = body[2]!["facts"]!.AsArray();

        Assert.Equal($"{severity.ToString().ToUpperInvariant()}｜ALARM", body[0]!["text"]!.GetValue<string>());
        Assert.Equal(expectedColor, body[0]!["color"]!.GetValue<string>());
        Assert.Equal(notification.Message, body[1]!["text"]!.GetValue<string>());
        Assert.Equal(notification.Tag, facts[0]!["value"]!.GetValue<string>());
        Assert.Equal("—", facts[1]!["value"]!.GetValue<string>());
        Assert.Equal("2026-10-10 14:30:15 +08:00", facts[3]!["value"]!.GetValue<string>());
        Assert.Equal(notification.EventId.ToString("D"), facts[4]!["value"]!.GetValue<string>());

        var reparsed = JsonNode.Parse(card.ToJsonString());
        Assert.NotNull(reparsed);
    }

    [Fact]
    public void Constructor_RejectsInvalidJson()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TeamsAdaptiveCardRenderer("{not-json}"));

        Assert.Equal("The Teams card template is not valid JSON.", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsUnsupportedVersion()
    {
        var template = File.ReadAllText(GetTemplatePath()).Replace(
            "\"version\": \"1.4\"",
            "\"version\": \"1.5\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TeamsAdaptiveCardRenderer(template));

        Assert.Contains("version must be 1.4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsMissingRequiredPlaceholder()
    {
        var template = File.ReadAllText(GetTemplatePath()).Replace(
            "{{eventId}}",
            "static value",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TeamsAdaptiveCardRenderer(template));

        Assert.Contains("eventId", exception.Message, StringComparison.Ordinal);
    }

    private static TeamsAdaptiveCardRenderer CreateRenderer() =>
        TeamsAdaptiveCardRenderer.LoadFromFile(GetTemplatePath());

    private static string GetTemplatePath() =>
        Path.Combine(AppContext.BaseDirectory, "templates", "teams-alarm-card.json");
}
