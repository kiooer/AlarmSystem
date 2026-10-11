using System.Globalization;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Infrastructure.Notifications.Line;

public sealed class LineAlarmTextRenderer
{
    public const int MaxTextLength = 5000;

    private const string EmptyDisplayValue = "—";

    public string Render(AlarmNotification notification)
    {
        var text = RenderCore(notification);
        if (text.Length > MaxTextLength)
        {
            throw new InvalidOperationException(
                $"The rendered LINE message exceeds {MaxTextLength} UTF-16 code units.");
        }

        return text;
    }

    internal bool TryRender(AlarmNotification notification, out string? text)
    {
        var rendered = RenderCore(notification);
        if (rendered.Length > MaxTextLength)
        {
            text = null;
            return false;
        }

        text = rendered;
        return true;
    }

    private static string RenderCore(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Message);

        var severityLabel = notification.Severity switch
        {
            AlarmSeverity.High => "HIGH",
            AlarmSeverity.Medium => "MEDIUM",
            AlarmSeverity.Low => "LOW",
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                notification.Severity,
                "Unknown severity."),
        };
        var statusLabel = notification.EventType switch
        {
            AlarmEventType.Alarm => "Active / UnACK",
            AlarmEventType.Ack => "Acknowledged",
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                notification.EventType,
                "Unknown event type."),
        };
        var area = string.IsNullOrWhiteSpace(notification.Area)
            ? EmptyDisplayValue
            : notification.Area;
        var equipment = string.IsNullOrWhiteSpace(notification.Equipment)
            ? EmptyDisplayValue
            : notification.Equipment;
        var sourceTimestamp = notification.SourceTimestamp.ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);

        return string.Join(
            '\n',
            "🔔 設備警報通知",
            $"等級：{severityLabel}",
            $"區域：{area}",
            $"設備：{equipment}",
            $"Tag：{notification.Tag}",
            $"警報內容：{notification.Message}",
            $"發生時間：{sourceTimestamp}",
            $"目前狀態：{statusLabel}",
            $"Alarm ID：{notification.EventId:D}",
            "發報系統：AlarmGateway",
            "請立即確認設備狀況");
    }
}
