using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Infrastructure.Notifications.Teams;

/// <summary>
/// 負責驗證 Teams Adaptive Card JSON 樣板，並將警報通知資料渲染成可供 Teams 通知通道使用的卡片物件。
/// </summary>
/// <remarks>
/// 此類別的責任包含：
/// <list type="bullet">
/// <item><description>解析樣板 JSON，並驗證根節點類型、Adaptive Card 版本、body 及必要 placeholder。</description></item>
/// <item><description>將事件識別碼、Tag、區域、事件類型、嚴重程度、訊息與來源時間填入樣板。</description></item>
/// <item><description>將 HIGH、MEDIUM、LOW 對應為 Attention、Warning、Good 顏色。</description></item>
/// <item><description>處理空白顯示值及包含 UTC offset 的時間格式。</description></item>
/// <item><description>透過 JSON DOM 寫入文字，確保引號、換行及 Unicode 等內容由序列化器安全跳脫。</description></item>
/// <item><description>每次渲染時複製原始樣板，避免不同通知之間共用或污染卡片狀態。</description></item>
/// </list>
/// 此類別不負責讀取或監控樣板檔案、HTTP 傳送、endpoint／權杖管理、重試排程、派送狀態保存或紀錄輸出；
/// 這些工作分別由樣板 provider、Teams 通知通道及後續的 outbox／派送流程負責。
/// </remarks>
public sealed partial class TeamsAdaptiveCardRenderer
{
    private const string EmptyDisplayValue = "—";
    private const string SupportedVersion = "1.4";

    private static readonly HashSet<string> RequiredPlaceholders =
    [
        "severityLabel",
        "eventTypeLabel",
        "adaptiveColor",
        "message",
        "tag",
        "areaDisplay",
        "sourceTimeDisplay",
        "eventId",
    ];

    private readonly JsonNode template;

    public TeamsAdaptiveCardRenderer(string templateJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateJson);

        try
        {
            template = JsonNode.Parse(templateJson)
                ?? throw new InvalidOperationException("The Teams card template is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Teams card template is not valid JSON.", exception);
        }

        ValidateTemplate(template);
    }

    public static TeamsAdaptiveCardRenderer LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TeamsAdaptiveCardRenderer(File.ReadAllText(path));
    }

    /// <summary>
    /// 將警報通知資料安全地填入已驗證的 Teams Adaptive Card 樣板。
    /// </summary>
    /// <param name="notification">
    /// 要顯示的警報事件；包含事件識別碼、Tag、區域、事件類型、嚴重程度、訊息及來源時間。
    /// </param>
    /// <returns>
    /// 可交由 Teams 通知通道包裝並序列化的 Adaptive Card JSON 物件。
    /// 每次呼叫都會建立獨立副本，不會修改原始樣板。
    /// </returns>
    /// <remarks>
    /// 此方法透過 JSON DOM 設定字串值，由序列化器負責特殊字元跳脫；同時套用嚴重程度顏色、
    /// 空白區域顯示值及包含 UTC offset 的來源時間格式。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> 為 null。</exception>
    /// <exception cref="ArgumentException">Tag 或訊息為 null、空字串或只包含空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">嚴重程度不是系統支援的列舉值。</exception>
    public JsonObject Render(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Message);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["severityLabel"] = notification.Severity.ToString().ToUpperInvariant(),
            ["eventTypeLabel"] = notification.EventType.ToString().ToUpperInvariant(),
            ["adaptiveColor"] = ToAdaptiveColor(notification.Severity),
            ["message"] = notification.Message,
            ["tag"] = notification.Tag,
            ["areaDisplay"] = string.IsNullOrWhiteSpace(notification.Area)
                ? EmptyDisplayValue
                : notification.Area,
            ["sourceTimeDisplay"] = notification.SourceTimestamp.ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture),
            ["eventId"] = notification.EventId.ToString("D"),
        };

        return (JsonObject)RenderNode(template.DeepClone(), values)!;
    }

    private static void ValidateTemplate(JsonNode root)
    {
        if (root is not JsonObject card ||
            card["type"]?.GetValue<string>() != "AdaptiveCard")
        {
            throw new InvalidOperationException(
                "The Teams card template root type must be AdaptiveCard.");
        }

        if (card["version"]?.GetValue<string>() != SupportedVersion)
        {
            throw new InvalidOperationException(
                $"The Teams card template version must be {SupportedVersion}.");
        }

        if (card["body"] is not JsonArray)
        {
            throw new InvalidOperationException("The Teams card template must contain a body array.");
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        CollectPlaceholders(root, found);

        var missing = RequiredPlaceholders.Except(found, StringComparer.Ordinal).ToArray();
        var unknown = found.Except(RequiredPlaceholders, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"The Teams card template has invalid placeholders. " +
                $"Missing: {string.Join(", ", missing)}. " +
                $"Unknown: {string.Join(", ", unknown)}.");
        }
    }

    private static void CollectPlaceholders(JsonNode? node, ISet<string> placeholders)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    CollectPlaceholders(property.Value, placeholders);
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    CollectPlaceholders(item, placeholders);
                }

                break;
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var value):
                foreach (Match match in PlaceholderPattern().Matches(value))
                {
                    placeholders.Add(match.Groups[1].Value);
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
                foreach (var replacement in values)
                {
                    value = value.Replace(
                        "{{" + replacement.Key + "}}",
                        replacement.Value,
                        StringComparison.Ordinal);
                }

                return JsonValue.Create(value);
            default:
                return node;
        }
    }

    private static string ToAdaptiveColor(AlarmSeverity severity) => severity switch
    {
        AlarmSeverity.High => "Attention",
        AlarmSeverity.Medium => "Warning",
        AlarmSeverity.Low => "Good",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown severity."),
    };

    [GeneratedRegex("\\{\\{([A-Za-z][A-Za-z0-9]*)\\}\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
