using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;
using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed partial class EmailMessageBuilder
{
    private static readonly HashSet<string> RequiredPlaceholders =
    [
        "severityLabel",
        "severityColor",
        "equipmentDisplay",
        "areaDisplay",
        "tag",
        "message",
        "sourceTimeDisplay",
        "eventId",
    ];

    public static void ValidateTemplate(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        var found = PlaceholderPattern()
            .Matches(template)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RequiredPlaceholders.Except(found, StringComparer.Ordinal).ToArray();
        var unknown = found.Except(RequiredPlaceholders, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new InvalidOperationException(
                "The Email template has invalid placeholders. " +
                $"Missing: {string.Join(", ", missing)}. " +
                $"Unknown: {string.Join(", ", unknown)}.");
        }
    }

    public EmailMessageContent Render(string template, AlarmNotification notification)
    {
        ValidateTemplate(template);
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Message);

        var (severityLabel, severityColor) = notification.Severity switch
        {
            AlarmSeverity.High => ("HIGH", "#C83C3C"),
            AlarmSeverity.Medium => ("MEDIUM", "#D97706"),
            AlarmSeverity.Low => ("LOW", "#2563EB"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                notification.Severity,
                "Unknown severity."),
        };
        var area = DisplayValue(notification.Area);
        var equipment = DisplayValue(notification.Equipment);
        var sourceTime = notification.SourceTimestamp.ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["severityLabel"] = WebUtility.HtmlEncode(severityLabel),
            ["severityColor"] = severityColor,
            ["equipmentDisplay"] = WebUtility.HtmlEncode(equipment),
            ["areaDisplay"] = WebUtility.HtmlEncode(area),
            ["tag"] = WebUtility.HtmlEncode(notification.Tag),
            ["message"] = EncodeMultiline(notification.Message),
            ["sourceTimeDisplay"] = WebUtility.HtmlEncode(sourceTime),
            ["eventId"] = notification.EventId.ToString("D"),
        };
        var htmlBody = PlaceholderPattern().Replace(
            template,
            match => values[match.Groups[1].Value]);
        var subject = $"[{severityLabel}] 設備警報通知 - " +
            $"{SanitizeHeader(area)} / {SanitizeHeader(equipment)}";
        var plainTextBody = string.Join(
            Environment.NewLine,
            $"{severityLabel}｜設備警報通知",
            string.Empty,
            notification.Message,
            string.Empty,
            $"設備：{equipment}",
            $"區域：{area}",
            $"Tag：{notification.Tag}",
            $"發生時間：{sourceTime}",
            $"Event ID：{notification.EventId:D}");

        return new EmailMessageContent(subject, htmlBody, plainTextBody);
    }

    public MimeMessage BuildMessage(EmailMessageContent content, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.SenderDisplayName, options.SenderAddress));
        foreach (var recipient in options.Recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = content.Subject;
        message.Body = new BodyBuilder
        {
            HtmlBody = content.HtmlBody,
            TextBody = content.PlainTextBody,
        }.ToMessageBody();
        return message;
    }

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string EncodeMultiline(string value) => WebUtility.HtmlEncode(value)
        .Replace("\r\n", "<br />", StringComparison.Ordinal)
        .Replace("\n", "<br />", StringComparison.Ordinal)
        .Replace("\r", "<br />", StringComparison.Ordinal);

    private static string SanitizeHeader(string value) => value
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    [GeneratedRegex("\\{\\{([A-Za-z][A-Za-z0-9]*)\\}\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
