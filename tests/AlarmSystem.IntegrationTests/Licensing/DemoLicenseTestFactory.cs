using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.IntegrationTests.Licensing;

internal sealed class DemoLicenseTestFactory : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public const string KeyId = "test-key-1";

    public IReadOnlyDictionary<string, byte[]> PublicKeys =>
        new Dictionary<string, byte[]>
        {
            [KeyId] = key.ExportSubjectPublicKeyInfo(),
        };

    public byte[] Create(
        string machineId,
        DateTimeOffset notBeforeUtc,
        int durationDays = 15,
        IReadOnlyList<NotificationFeature>? features = null,
        string productId = "AlarmSystem",
        string licenseType = "DEMO",
        string keyId = KeyId,
        string algorithm = "ES256")
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, kid = keyId });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            productId,
            licenseId = "DEMO-TEST-0001",
            licenseType,
            machineIdHash = machineId,
            issuedAtUtc = FormatUtc(notBeforeUtc),
            notBeforeUtc = FormatUtc(notBeforeUtc),
            expiresAtUtc = FormatUtc(notBeforeUtc.AddDays(durationDays)),
            features = (features ?? Enum.GetValues<NotificationFeature>()).Select(value => (int)value),
        });
        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encoding.ASCII.GetBytes($"{signingInput}.{Encode(signature)}");
    }

    public byte[] CreateRaw(string headerJson, string payloadJson)
    {
        var signingInput = $"{Encode(Encoding.UTF8.GetBytes(headerJson))}.{Encode(Encoding.UTF8.GetBytes(payloadJson))}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encoding.ASCII.GetBytes($"{signingInput}.{Encode(signature)}");
    }

    public void Dispose() => key.Dispose();

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
