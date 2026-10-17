using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlarmSystem.Infrastructure.Licensing.Cryptography;

public enum JwsLicenseFailureKind
{
    Format = 0,
    Algorithm = 1,
    UntrustedKey = 2,
    Signature = 3,
}

public sealed class JwsLicenseException(
    JwsLicenseFailureKind failureKind,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public JwsLicenseFailureKind FailureKind { get; } = failureKind;
}

public sealed record VerifiedLicenseJws(string KeyId, byte[] Payload);

public static class JwsCompactLicenseVerifier
{
    private static readonly HashSet<string> HeaderFields = ["alg", "kid"];

    public static VerifiedLicenseJws Verify(
        ReadOnlyMemory<byte> licenseContent,
        ILicensePublicKeyTrustStore trustStore)
    {
        ArgumentNullException.ThrowIfNull(trustStore);

        try
        {
            var compact = new UTF8Encoding(false, true).GetString(licenseContent.Span).Trim();
            var parts = compact.Split('.');
            if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
            {
                throw new JwsLicenseException(
                    JwsLicenseFailureKind.Format,
                    "The license is not a JWS compact document.");
            }

            using var header = StrictLicenseJson.ParseObject(
                DecodeBase64Url(parts[0]),
                HeaderFields);
            var algorithm = StrictLicenseJson.ReadRequiredString(header.RootElement, "alg", 16);
            var keyId = StrictLicenseJson.ReadRequiredString(header.RootElement, "kid", 128);
            if (!string.Equals(algorithm, "ES256", StringComparison.Ordinal))
            {
                throw new JwsLicenseException(
                    JwsLicenseFailureKind.Algorithm,
                    "The JWS algorithm is not supported.");
            }

            if (!trustStore.TryGetPublicKey(keyId, out var publicKey))
            {
                throw new JwsLicenseException(
                    JwsLicenseFailureKind.UntrustedKey,
                    "The JWS key identifier is not trusted.");
            }

            var signature = DecodeBase64Url(parts[2]);
            if (signature.Length != 64)
            {
                throw new JwsLicenseException(
                    JwsLicenseFailureKind.Signature,
                    "The ES256 signature encoding is invalid.");
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
                throw new JwsLicenseException(
                    JwsLicenseFailureKind.Signature,
                    "The license signature is invalid.");
            }

            return new VerifiedLicenseJws(keyId, DecodeBase64Url(parts[1]));
        }
        catch (JwsLicenseException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or CryptographicException or
            DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            throw new JwsLicenseException(
                JwsLicenseFailureKind.Format,
                "The license format or signature is invalid.",
                exception);
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
