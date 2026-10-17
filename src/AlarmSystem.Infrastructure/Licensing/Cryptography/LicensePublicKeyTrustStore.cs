namespace AlarmSystem.Infrastructure.Licensing.Cryptography;

public interface ILicensePublicKeyTrustStore
{
    bool TryGetPublicKey(string keyId, out ReadOnlyMemory<byte> subjectPublicKeyInfo);
}
