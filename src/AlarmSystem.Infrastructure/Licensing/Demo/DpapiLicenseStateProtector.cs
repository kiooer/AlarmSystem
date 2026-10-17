using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

[SupportedOSPlatform("windows")]
public sealed class DpapiLicenseStateProtector : ILicenseStateProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
        "AlarmSystem.DemoLicense.State.v1");

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
            throw new PlatformNotSupportedException("DPAPI demo license state requires Windows.");
        }
    }
}
