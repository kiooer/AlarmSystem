using System.Collections.ObjectModel;
using AlarmSystem.Infrastructure.Licensing.Cryptography;

namespace AlarmSystem.Infrastructure.Licensing.Software;

public interface ISoftwareLicenseTrustStore : ILicensePublicKeyTrustStore;

public sealed class SoftwareLicenseTrustStore : ISoftwareLicenseTrustStore
{
    private readonly IReadOnlyDictionary<string, byte[]> keys;

    public SoftwareLicenseTrustStore() : this(new Dictionary<string, byte[]>())
    {
    }

    public SoftwareLicenseTrustStore(IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        this.keys = new ReadOnlyDictionary<string, byte[]>(
            keys.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));
    }

    public bool TryGetPublicKey(string keyId, out ReadOnlyMemory<byte> subjectPublicKeyInfo)
    {
        if (keys.TryGetValue(keyId, out var key))
        {
            subjectPublicKeyInfo = key;
            return true;
        }

        subjectPublicKeyInfo = default;
        return false;
    }
}
