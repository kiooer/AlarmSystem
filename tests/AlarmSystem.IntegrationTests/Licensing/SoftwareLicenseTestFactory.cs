using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.IntegrationTests.Licensing;

internal sealed class SoftwareLicenseTestFactory : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public const string KeyId = "runtime-test-key";

    public IReadOnlyDictionary<string, byte[]> PublicKeys =>
        new Dictionary<string, byte[]>
        {
            [KeyId] = key.ExportSubjectPublicKeyInfo(),
        };

    public byte[] Create(
        string machineId,
        DateTimeOffset notBeforeUtc,
        string licenseType = "DEMO",
        string bindingMode = "MACHINE",
        int durationDays = 45,
        string licenseId = "SOFTWARE-TEST-0001",
        IReadOnlyList<NotificationFeature>? features = null,
        string productId = "AlarmSystem",
        int formatVersion = 2,
        string keyId = KeyId,
        string algorithm = "ES256",
        bool forceExpiration = false,
        string? machineIdHashOverride = null)
    {
        var isPerpetual = string.Equals(licenseType, "PERPETUAL", StringComparison.Ordinal);
        var machineIdHash = string.Equals(bindingMode, "NONE", StringComparison.Ordinal)
            ? null
            : machineIdHashOverride ?? machineId;
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, kid = keyId });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion,
            productId,
            licenseId,
            licenseType,
            bindingMode,
            machineIdHash,
            issuedAtUtc = FormatUtc(notBeforeUtc),
            notBeforeUtc = FormatUtc(notBeforeUtc),
            expiresAtUtc = isPerpetual && !forceExpiration
                ? null
                : FormatUtc(notBeforeUtc.AddDays(durationDays)),
            features = (features ?? Enum.GetValues<NotificationFeature>())
                .Select(value => (int)value),
        });
        return Sign(header, payload);
    }

    public byte[] CreateRaw(string headerJson, string payloadJson) =>
        Sign(Encoding.UTF8.GetBytes(headerJson), Encoding.UTF8.GetBytes(payloadJson));

    public void Dispose() => key.Dispose();

    private byte[] Sign(byte[] header, byte[] payload)
    {
        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encoding.ASCII.GetBytes($"{signingInput}.{Encode(signature)}");
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
