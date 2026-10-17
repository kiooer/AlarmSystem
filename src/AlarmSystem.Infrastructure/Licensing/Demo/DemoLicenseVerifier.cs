using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed class DemoLicenseVerifier(IDemoLicenseTrustStore trustStore)
{
    private static readonly HashSet<string> HeaderFields = ["alg", "kid"];
    private static readonly HashSet<string> PayloadFields =
    [
        "formatVersion", "productId", "licenseId", "licenseType", "machineIdHash",
        "issuedAtUtc", "notBeforeUtc", "expiresAtUtc", "features",
    ];

    public LicenseValidationResult Verify(
        ReadOnlyMemory<byte> licenseContent,
        string machineId,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        nowUtc = nowUtc.ToUniversalTime();

        try
        {
            var compact = new UTF8Encoding(false, true).GetString(licenseContent.Span).Trim();
            var parts = compact.Split('.');
            if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
            {
                return Invalid(nowUtc, "The license is not a JWS compact document.");
            }

            using var header = ParseStrictObject(DecodeBase64Url(parts[0]), HeaderFields);
            var algorithm = ReadRequiredString(header.RootElement, "alg", 16);
            var keyId = ReadRequiredString(header.RootElement, "kid", 128);
            if (!string.Equals(algorithm, "ES256", StringComparison.Ordinal))
            {
                return Invalid(nowUtc, "The JWS algorithm is not supported.");
            }

            if (!trustStore.TryGetPublicKey(keyId, out var publicKey))
            {
                return Invalid(nowUtc, "The JWS key identifier is not trusted.");
            }

            var signature = DecodeBase64Url(parts[2]);
            if (signature.Length != 64)
            {
                return Invalid(nowUtc, "The ES256 signature encoding is invalid.");
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey.Span, out var bytesRead);
            if (bytesRead != publicKey.Length ||
                !ecdsa.VerifyData(
                    Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return Invalid(nowUtc, "The license signature is invalid.");
            }

            using var payload = ParseStrictObject(DecodeBase64Url(parts[1]), PayloadFields);
            var document = ParsePayload(payload.RootElement, keyId);
            return ValidateClaims(document, machineId, nowUtc);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or CryptographicException or
            DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            return Invalid(nowUtc, "The license format or signature is invalid.");
        }
    }

    private static DemoLicenseDocument ParsePayload(JsonElement payload, string keyId)
    {
        var formatVersion = payload.GetProperty("formatVersion").GetInt32();
        var featuresElement = payload.GetProperty("features");
        if (featuresElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("features must be an array.");
        }

        var features = featuresElement.EnumerateArray()
            .Select(value => (NotificationFeature)value.GetInt32())
            .ToArray();
        if (features.Length == 0 || features.Distinct().Count() != features.Length ||
            features.Any(feature => !Enum.IsDefined(feature)))
        {
            throw new JsonException("features contains unsupported or duplicate values.");
        }

        return new DemoLicenseDocument(
            formatVersion,
            ReadRequiredString(payload, "productId", 64),
            ReadRequiredString(payload, "licenseId", 128),
            ReadRequiredString(payload, "licenseType", 16),
            ReadRequiredString(payload, "machineIdHash", 128),
            ReadUtc(payload, "issuedAtUtc"),
            ReadUtc(payload, "notBeforeUtc"),
            ReadUtc(payload, "expiresAtUtc"),
            features,
            keyId);
    }

    private static LicenseValidationResult ValidateClaims(
        DemoLicenseDocument document,
        string machineId,
        DateTimeOffset nowUtc)
    {
        var machineMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(document.MachineIdHash.ToUpperInvariant()),
            Encoding.ASCII.GetBytes(machineId.ToUpperInvariant()));
        var state = LicenseState.Invalid;
        var failureCode = LicenseAuthorizationFailureCodes.ValidationFailed;
        var diagnostic = "The license claims are invalid.";

        if (document.FormatVersion != 1 ||
            !string.Equals(document.ProductId, "AlarmSystem", StringComparison.Ordinal) ||
            !string.Equals(document.LicenseType, "DEMO", StringComparison.Ordinal) ||
            document.IssuedAtUtc > document.NotBeforeUtc ||
            (document.ExpiresAtUtc - document.NotBeforeUtc != TimeSpan.FromDays(15) &&
             document.ExpiresAtUtc - document.NotBeforeUtc != TimeSpan.FromDays(30)))
        {
            diagnostic = "The license product, type, version, or duration is invalid.";
        }
        else if (!machineMatches)
        {
            diagnostic = "The license was issued for a different machine.";
        }
        else if (nowUtc < document.NotBeforeUtc)
        {
            diagnostic = "The license is not active yet.";
        }
        else if (nowUtc >= document.ExpiresAtUtc)
        {
            state = LicenseState.Expired;
            failureCode = LicenseAuthorizationFailureCodes.Expired;
            diagnostic = "The demo license has expired.";
        }
        else
        {
            var status = CreateStatus(document, nowUtc, LicenseState.Active, true, null, "Demo license is active.");
            return LicenseValidationResult.Valid(status);
        }

        var invalidStatus = CreateStatus(
            document,
            nowUtc,
            state,
            machineMatches,
            failureCode,
            diagnostic);
        return LicenseValidationResult.Invalid(invalidStatus, failureCode, diagnostic);
    }

    private static LicenseStatus CreateStatus(
        DemoLicenseDocument document,
        DateTimeOffset nowUtc,
        LicenseState state,
        bool machineMatches,
        string? failureCode,
        string diagnostic) =>
        new(
            LicenseSource.Demo,
            state,
            document.LicenseId,
            "Demo",
            document.IssuedAtUtc,
            document.NotBeforeUtc,
            document.ExpiresAtUtc,
            Math.Max(0, (int)Math.Ceiling((document.ExpiresAtUtc - nowUtc).TotalDays)),
            machineMatches,
            document.Features,
            nowUtc,
            failureCode,
            diagnostic);

    private static LicenseValidationResult Invalid(DateTimeOffset nowUtc, string diagnostic)
    {
        var status = new LicenseStatus(
            LicenseSource.Demo,
            LicenseState.Invalid,
            null,
            "Demo",
            null,
            null,
            null,
            null,
            null,
            [],
            nowUtc,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);
        return LicenseValidationResult.Invalid(
            status,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);
    }

    private static JsonDocument ParseStrictObject(byte[] json, HashSet<string> allowedFields)
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

    private static string ReadRequiredString(JsonElement element, string propertyName, int maximumLength)
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

    private static DateTimeOffset ReadUtc(JsonElement element, string propertyName)
    {
        var text = ReadRequiredString(element, propertyName, 40);
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
