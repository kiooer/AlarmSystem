using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AlarmSystem.Infrastructure.Licensing.Software;

[SupportedOSPlatform("windows")]
public sealed class DpapiSoftwareLicenseStateProtector : ISoftwareLicenseStateProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
        "AlarmSystem.SoftwareLicense.State.v2");

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        EnsureWindows();
        return ProtectedData.Protect(
            plaintext.ToArray(),
            Entropy,
            DataProtectionScope.LocalMachine);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        EnsureWindows();
        return ProtectedData.Unprotect(
            protectedData.ToArray(),
            Entropy,
            DataProtectionScope.LocalMachine);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI software license state requires Windows.");
        }
    }
}
