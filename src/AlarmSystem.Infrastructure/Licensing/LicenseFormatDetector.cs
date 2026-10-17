using System.Text;
using System.Text.Json;

namespace AlarmSystem.Infrastructure.Licensing;

public static class LicenseFormatDetector
{
    public static int? TryDetect(ReadOnlyMemory<byte> licenseContent)
    {
        try
        {
            var compact = System.Text.Encoding.UTF8.GetString(licenseContent.Span).Trim();
            var parts = compact.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var formatVersions = payload.RootElement.EnumerateObject()
                .Where(property => property.NameEquals("formatVersion"))
                .ToArray();
            return formatVersions.Length == 1 &&
                formatVersions[0].Value.ValueKind == JsonValueKind.Number &&
                formatVersions[0].Value.TryGetInt32(out var version)
                    ? version
                    : null;
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (value.Contains('=') || value.Any(character =>
            !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new FormatException("The base64url value is invalid.");
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("The base64url value has an invalid length."),
        };
        return Convert.FromBase64String(base64);
    }
}
