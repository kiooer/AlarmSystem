using System.Collections.ObjectModel;
using AlarmSystem.Infrastructure.Licensing.Cryptography;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public interface IDemoLicenseTrustStore : ILicensePublicKeyTrustStore;

public sealed class DemoLicenseTrustStore : IDemoLicenseTrustStore
{
    private readonly IReadOnlyDictionary<string, byte[]> keys;

    public DemoLicenseTrustStore() : this(new Dictionary<string, byte[]>())
    {
    }

    public DemoLicenseTrustStore(IReadOnlyDictionary<string, byte[]> keys)
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
