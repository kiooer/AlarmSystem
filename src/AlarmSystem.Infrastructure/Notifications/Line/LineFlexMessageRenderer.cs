using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Infrastructure.Notifications.Line;

public sealed partial class LineFlexMessageRenderer
{
    public const int MaxBubbleJsonBytes = 30 * 1024;

    private static readonly HashSet<string> RequiredPlaceholders =
    [
        "severityLabel",
        "lineColor",
        "statusColor",
        "areaDisplay",
        "equipmentDisplay",
        "tag",
        "message",
        "sourceTimeDisplay",
        "statusLabel",
        "eventId",
    ];

    private readonly JsonObject template;

    public LineFlexMessageRenderer(string templateJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateJson);

        try
        {
            template = JsonNode.Parse(templateJson) as JsonObject
                ?? throw new InvalidOperationException("The LINE Flex template root must be a bubble object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The LINE Flex template is not valid JSON.", exception);
        }

        ValidateTemplate(template);
    }

    public static LineFlexMessageRenderer LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new LineFlexMessageRenderer(File.ReadAllText(path));
    }

    public JsonObject Render(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Message);

        var (severityLabel, lineColor) = notification.Severity switch
        {
            AlarmSeverity.High => ("HIGH", "#C83C3C"),
            AlarmSeverity.Medium => ("MEDIUM", "#D99A22"),
            AlarmSeverity.Low => ("LOW", "#27865A"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                notification.Severity,
                "Unknown severity."),
        };
        var (statusLabel, statusColor) = notification.EventType switch
        {
            AlarmEventType.Alarm => ("Active / UnACK", "#C83C3C"),
            AlarmEventType.Ack => ("Acknowledged", "#27865A"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                notification.EventType,
                "Unknown event type."),
        };

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["severityLabel"] = severityLabel,
            ["lineColor"] = lineColor,
            ["statusColor"] = statusColor,
            ["areaDisplay"] = string.IsNullOrWhiteSpace(notification.Area)
                ? "—"
                : notification.Area,
            ["equipmentDisplay"] = string.IsNullOrWhiteSpace(notification.Equipment)
                ? "—"
                : notification.Equipment,
            ["tag"] = notification.Tag,
            ["message"] = notification.Message,
            ["sourceTimeDisplay"] = notification.SourceTimestamp.ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture),
            ["statusLabel"] = statusLabel,
            ["eventId"] = notification.EventId.ToString("D"),
        };

        var bubble = (JsonObject)RenderNode(template.DeepClone(), values)!;
        if (Encoding.UTF8.GetByteCount(bubble.ToJsonString()) > MaxBubbleJsonBytes)
        {
            throw new LineFlexMessageTooLargeException();
        }

        return bubble;
    }

    private static void ValidateTemplate(JsonObject bubble)
    {
        if (bubble["type"]?.ToString() != "bubble")
        {
            throw new InvalidOperationException("The LINE Flex template root type must be bubble.");
        }

        foreach (var blockName in new[] { "header", "body", "footer" })
        {
            if (bubble[blockName] is not JsonObject block ||
                block["type"]?.ToString() != "box" ||
                block["contents"] is not JsonArray contents ||
                contents.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The LINE Flex template must contain a nonempty {blockName} box.");
            }
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        CollectPlaceholders(bubble, found);
        var missing = RequiredPlaceholders.Except(found, StringComparer.Ordinal).ToArray();
        var unknown = found.Except(RequiredPlaceholders, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new InvalidOperationException(
                "The LINE Flex template has invalid placeholders. " +
                $"Missing: {string.Join(", ", missing)}. " +
                $"Unknown: {string.Join(", ", unknown)}.");
        }
    }

    private static void CollectPlaceholders(JsonNode? node, ISet<string> found)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    CollectPlaceholders(property.Value, found);
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    CollectPlaceholders(item, found);
                }

                break;
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var value):
                foreach (Match match in PlaceholderPattern().Matches(value))
                {
                    found.Add(match.Groups[1].Value);
                }

                break;
        }
    }

    private static JsonNode? RenderNode(JsonNode? node, IReadOnlyDictionary<string, string> values)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var propertyName in jsonObject.Select(property => property.Key).ToArray())
                {
                    var current = jsonObject[propertyName];
                    var rendered = RenderNode(current, values);
                    if (!ReferenceEquals(current, rendered))
                    {
                        jsonObject[propertyName] = rendered;
                    }
                }

                return jsonObject;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var current = jsonArray[index];
                    var rendered = RenderNode(current, values);
                    if (!ReferenceEquals(current, rendered))
                    {
                        jsonArray[index] = rendered;
                    }
                }

                return jsonArray;
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var value):
                return JsonValue.Create(PlaceholderPattern().Replace(
                    value,
                    match => values[match.Groups[1].Value]));
            default:
                return node;
        }
    }

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}

public sealed class LineFlexMessageTooLargeException()
    : InvalidOperationException("The rendered LINE Flex bubble exceeds 30 KB.");
