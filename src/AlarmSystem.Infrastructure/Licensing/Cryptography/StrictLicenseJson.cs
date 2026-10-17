using System.Globalization;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Cryptography;

internal static class StrictLicenseJson
{
    public static JsonDocument ParseObject(byte[] json, HashSet<string> allowedFields)
    {
        var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new JsonException("A JSON object is required.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name) || !names.Add(property.Name))
            {
                document.Dispose();
                throw new JsonException("Unknown or duplicate JSON property.");
            }
        }

        if (!allowedFields.SetEquals(names))
        {
            document.Dispose();
            throw new JsonException("Required JSON properties are missing.");
        }

        return document;
    }

    public static string ReadRequiredString(
        JsonElement element,
        string propertyName,
        int maximumLength)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{propertyName} must be a string.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text != text.Trim())
        {
            throw new JsonException($"{propertyName} is invalid.");
        }

        return text;
    }

    public static string? ReadNullableString(
        JsonElement element,
        string propertyName,
        int maximumLength)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadRequiredString(element, propertyName, maximumLength);
    }

    public static DateTimeOffset ReadUtc(JsonElement element, string propertyName)
    {
        var text = ReadRequiredString(element, propertyName, 40);
        return ParseUtc(text, propertyName);
    }

    public static DateTimeOffset? ReadNullableUtc(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ParseUtc(ReadRequiredString(element, propertyName, 40), propertyName);
    }

    public static IReadOnlyList<NotificationFeature> ReadFeatures(
        JsonElement element,
        string propertyName)
    {
        var featuresElement = element.GetProperty(propertyName);
        if (featuresElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"{propertyName} must be an array.");
        }

        var features = featuresElement.EnumerateArray()
            .Select(value => (NotificationFeature)value.GetInt32())
            .ToArray();
        if (features.Length == 0 || features.Distinct().Count() != features.Length ||
            features.Any(feature => !Enum.IsDefined(feature)))
        {
            throw new JsonException($"{propertyName} contains unsupported or duplicate values.");
        }

        return features;
    }

    private static DateTimeOffset ParseUtc(string text, string propertyName)
    {
        if (!text.EndsWith('Z') ||
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value) ||
            value.Offset != TimeSpan.Zero)
        {
            throw new JsonException($"{propertyName} must be UTC with a Z suffix.");
        }

        return value;
    }
}
