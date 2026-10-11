using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.IntegrationTests.Notifications.Line;

public sealed class LineAlarmTextRendererTests
{
    private readonly LineAlarmTextRenderer renderer = new();

    [Fact]
    public void Render_RendersAllFieldsInFixedTraditionalChineseFormat()
    {
        var notification = new AlarmNotification(
            Guid.Parse("8b7d749e-04db-41fd-a3a0-21bf9a146b63"),
            "溫度.Tag\"A",
            "一號廠區",
            AlarmEventType.Alarm,
            AlarmSeverity.High,
            "第一行\n第二行 \"quoted\"",
            new DateTimeOffset(2026, 10, 10, 14, 30, 15, TimeSpan.FromHours(8)),
            "空壓機 A");

        var text = renderer.Render(notification);

        Assert.Equal(
            "🔔 設備警報通知\n" +
            "等級：HIGH\n" +
            "區域：一號廠區\n" +
            "設備：空壓機 A\n" +
            "Tag：溫度.Tag\"A\n" +
            "警報內容：第一行\n第二行 \"quoted\"\n" +
            "發生時間：2026-10-10 14:30:15 +08:00\n" +
            "目前狀態：Active / UnACK\n" +
            "Alarm ID：8b7d749e-04db-41fd-a3a0-21bf9a146b63\n" +
            "發報系統：AlarmGateway\n" +
            "請立即確認設備狀況",
            text);
    }

    [Theory]
    [InlineData(AlarmEventType.Alarm, "目前狀態：Active / UnACK")]
    [InlineData(AlarmEventType.Ack, "目前狀態：Acknowledged")]
    public void Render_MapsEventStatus(AlarmEventType eventType, string expectedLine)
    {
        var text = renderer.Render(CreateNotification(eventType: eventType));

        Assert.Contains(expectedLine, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AlarmSeverity.High, "等級：HIGH")]
    [InlineData(AlarmSeverity.Medium, "等級：MEDIUM")]
    [InlineData(AlarmSeverity.Low, "等級：LOW")]
    public void Render_MapsSeverity(AlarmSeverity severity, string expectedLine)
    {
        var text = renderer.Render(CreateNotification(severity: severity));

        Assert.Contains(expectedLine, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "\t")]
    public void Render_UsesDashForBlankAreaAndEquipment(string? area, string? equipment)
    {
        var text = renderer.Render(CreateNotification(area: area, equipment: equipment));

        Assert.Contains("區域：—", text, StringComparison.Ordinal);
        Assert.Contains("設備：—", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PreservesSourceTimestampOffset()
    {
        var notification = CreateNotification() with
        {
            SourceTimestamp = new DateTimeOffset(
                2026,
                12,
                31,
                23,
                59,
                58,
                TimeSpan.FromHours(-5)),
        };

        var text = renderer.Render(notification);

        Assert.Contains("發生時間：2026-12-31 23:59:58 -05:00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RejectsUnknownSeverity()
    {
        var notification = CreateNotification(severity: (AlarmSeverity)999);

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(notification));
    }

    [Fact]
    public void Render_RejectsUnknownEventType()
    {
        var notification = CreateNotification(eventType: (AlarmEventType)999);

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(notification));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_RejectsBlankTagOrMessage(bool blankTag)
    {
        var notification = CreateNotification(
            tag: blankTag ? "   " : "TEST.TAG",
            message: blankTag ? "Test message" : "\t");

        Assert.Throws<ArgumentException>(() => renderer.Render(notification));
    }

    [Fact]
    public void Render_AllowsExactlyFiveThousandUtf16CodeUnits()
    {
        var notification = CreateNotificationWithRenderedLength(LineAlarmTextRenderer.MaxTextLength);

        var text = renderer.Render(notification);

        Assert.Equal(LineAlarmTextRenderer.MaxTextLength, text.Length);
    }

    [Fact]
    public void Render_RejectsFiveThousandAndOneUtf16CodeUnits()
    {
        var notification = CreateNotificationWithRenderedLength(
            LineAlarmTextRenderer.MaxTextLength + 1);

        var exception = Assert.Throws<InvalidOperationException>(
            () => renderer.Render(notification));

        Assert.Contains("5000", exception.Message, StringComparison.Ordinal);
    }

    private AlarmNotification CreateNotificationWithRenderedLength(int targetLength)
    {
        var oneCharacterMessage = CreateNotification(message: "界");
        var fixedLength = renderer.Render(oneCharacterMessage).Length - 1;
        return CreateNotification(message: new string('界', targetLength - fixedLength));
    }

    private static AlarmNotification CreateNotification(
        string tag = "TEST.TAG",
        string? area = "Test area",
        AlarmEventType eventType = AlarmEventType.Alarm,
        AlarmSeverity severity = AlarmSeverity.Medium,
        string message = "Test message",
        string? equipment = "Test equipment") => new(
            Guid.Parse("e0dc4cca-d31c-4c55-97c3-a6e2311fb837"),
            tag,
            area,
            eventType,
            severity,
            message,
            new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
            equipment);
}
