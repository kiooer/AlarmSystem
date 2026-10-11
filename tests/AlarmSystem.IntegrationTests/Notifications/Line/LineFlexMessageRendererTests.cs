using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.IntegrationTests.Notifications.Line;

public sealed class LineFlexMessageRendererTests
{
    private static readonly string TemplateJson = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "templates",
        "line-alarm-card.json"));

    private readonly LineFlexMessageRenderer renderer = new(TemplateJson);

    [Fact]
    public void Render_HighAlarmPreservesTeamsFieldOrderAndCompleteData()
    {
        var notification = CreateNotification();

        var bubble = renderer.Render(notification);
        var body = bubble["body"]!["contents"]!.AsArray();

        Assert.Equal("bubble", bubble["type"]!.GetValue<string>());
        Assert.Equal("🔔 設備警報通知", bubble["header"]!["contents"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("警報等級：HIGH", bubble["header"]!["contents"]![1]!["text"]!.GetValue<string>());
        Assert.Equal("#C83C3C", bubble["styles"]!["header"]!["backgroundColor"]!.GetValue<string>());
        Assert.Equal(
            ["區域", "設備", "Tag", "警報內容", "發生時間", "目前狀態", "Alarm ID", "發報系統"],
            body.Select(row => row!["contents"]![0]!["text"]!.GetValue<string>()));
        Assert.Equal("測試區域", GetValue(body, 0));
        Assert.Equal("測試設備", GetValue(body, 1));
        Assert.Equal("TEST.TAG", GetValue(body, 2));
        Assert.Equal("警報內容", body[3]!["contents"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("警報訊息", GetValue(body, 3));
        Assert.Equal("bold", body[3]!["contents"]![1]!["weight"]!.GetValue<string>());
        Assert.True(body[3]!["contents"]![1]!["wrap"]!.GetValue<bool>());
        Assert.Equal("2026-10-11 08:30:00 +08:00", GetValue(body, 4));
        Assert.Equal("Active / UnACK", GetValue(body, 5));
        Assert.Equal("#C83C3C", body[5]!["contents"]![1]!["color"]!.GetValue<string>());
        Assert.Equal(notification.EventId.ToString("D"), GetValue(body, 6));
        Assert.True(body[6]!["contents"]![1]!["wrap"]!.GetValue<bool>());
        Assert.Equal("AlarmGateway", GetValue(body, 7));
        Assert.Equal("ⓘ 請立即確認設備狀況", bubble["footer"]!["contents"]![0]!["text"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(AlarmSeverity.High, "HIGH", "#C83C3C")]
    [InlineData(AlarmSeverity.Medium, "MEDIUM", "#D99A22")]
    [InlineData(AlarmSeverity.Low, "LOW", "#27865A")]
    public void Render_MapsSeverityToLineColor(
        AlarmSeverity severity,
        string label,
        string color)
    {
        var bubble = renderer.Render(CreateNotification() with { Severity = severity });

        Assert.Equal("警報等級：" + label, bubble["header"]!["contents"]![1]!["text"]!.GetValue<string>());
        Assert.Equal(color, bubble["styles"]!["header"]!["backgroundColor"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(AlarmEventType.Alarm, "Active / UnACK", "#C83C3C")]
    [InlineData(AlarmEventType.Ack, "Acknowledged", "#27865A")]
    public void Render_MapsEventStatusAndColor(
        AlarmEventType eventType,
        string label,
        string color)
    {
        var bubble = renderer.Render(CreateNotification() with { EventType = eventType });
        var statusRow = bubble["body"]!["contents"]![5]!;

        Assert.Equal(label, statusRow["contents"]![1]!["text"]!.GetValue<string>());
        Assert.Equal(color, statusRow["contents"]![1]!["color"]!.GetValue<string>());
    }

    [Fact]
    public void Render_UsesDashForBlankAreaAndEquipment()
    {
        var bubble = renderer.Render(CreateNotification() with { Area = " ", Equipment = null });
        var body = bubble["body"]!["contents"]!.AsArray();

        Assert.Equal("—", GetValue(body, 0));
        Assert.Equal("—", GetValue(body, 1));
    }

    [Fact]
    public void Render_PreservesQuotesBackslashNewlinesAndUnicodeAsValidJson()
    {
        const string message = "測試 \"引號\" \\ 路徑\n第二行 🚨";
        const string tag = "TAG\"\n設備";
        var bubble = renderer.Render(CreateNotification() with { Message = message, Tag = tag });
        var parsed = JsonNode.Parse(bubble.ToJsonString())!.AsObject();
        var body = parsed["body"]!["contents"]!.AsArray();

        Assert.Equal(tag, GetValue(body, 2));
        Assert.Equal(message, GetValue(body, 3));
    }

    [Fact]
    public void Render_CreatesIndependentBubblesForDifferentEvents()
    {
        var first = renderer.Render(CreateNotification() with { Message = "第一筆" });
        var second = renderer.Render(CreateNotification() with { Message = "第二筆" });

        Assert.Equal("第一筆", GetValue(first["body"]!["contents"]!.AsArray(), 3));
        Assert.Equal("第二筆", GetValue(second["body"]!["contents"]!.AsArray(), 3));
        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("not-json", "valid JSON")]
    [InlineData("{}", "root type")]
    public void Constructor_RejectsMalformedTemplate(string template, string expectedReason)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new LineFlexMessageRenderer(template));

        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsMissingOrUnknownPlaceholder()
    {
        var missing = TemplateJson.Replace("{{eventId}}", "no-id", StringComparison.Ordinal);
        var unknown = TemplateJson.Replace("{{eventId}}", "{{unknownField}}", StringComparison.Ordinal);

        Assert.Contains("eventId", Assert.Throws<InvalidOperationException>(
            () => new LineFlexMessageRenderer(missing)).Message, StringComparison.Ordinal);
        Assert.Contains("unknownField", Assert.Throws<InvalidOperationException>(
            () => new LineFlexMessageRenderer(unknown)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsMissingBodyBlock()
    {
        var template = JsonNode.Parse(TemplateJson)!.AsObject();
        template.Remove("body");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new LineFlexMessageRenderer(template.ToJsonString()));

        Assert.Contains("body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RejectsUnknownEnumAndBlankRequiredFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => renderer.Render(CreateNotification() with { Severity = (AlarmSeverity)99 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => renderer.Render(CreateNotification() with { EventType = (AlarmEventType)99 }));
        Assert.Throws<ArgumentException>(
            () => renderer.Render(CreateNotification() with { Tag = " " }));
        Assert.Throws<ArgumentException>(
            () => renderer.Render(CreateNotification() with { Message = " " }));
    }

    [Fact]
    public void Render_RejectsOversizedBubbleWithoutTruncatingData()
    {
        var notification = CreateNotification() with
        {
            Message = new string('界', LineFlexMessageRenderer.MaxBubbleJsonBytes),
        };

        Assert.Throws<LineFlexMessageTooLargeException>(() => renderer.Render(notification));
    }

    private static string GetValue(JsonArray rows, int index) =>
        rows[index]!["contents"]![1]!["text"]!.GetValue<string>();

    private static AlarmNotification CreateNotification() => new(
        Guid.Parse("d949ea2c-87e5-4a70-8f99-627d07df2da0"),
        "TEST.TAG",
        "測試區域",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "警報訊息",
        new DateTimeOffset(2026, 10, 11, 8, 30, 0, TimeSpan.FromHours(8)),
        "測試設備");
}
