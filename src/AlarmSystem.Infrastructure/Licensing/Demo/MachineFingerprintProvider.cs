using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public interface IMachineFingerprintProvider
{
    string GetMachineId();
}

[SupportedOSPlatform("windows")]
public sealed class MachineFingerprintProvider : IMachineFingerprintProvider
{
    public string GetMachineId()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Demo licensing requires Windows machine identity.");
        }

        var components = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["machineGuid"] = ReadRegistryValue(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Cryptography",
                "MachineGuid"),
            ["baseBoard"] = ReadRegistryValue(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                @"HARDWARE\DESCRIPTION\System\BIOS",
                "BaseBoardProduct"),
            ["systemProduct"] = ReadRegistryValue(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                @"HARDWARE\DESCRIPTION\System\BIOS",
                "SystemProductName"),
        };

        if (string.IsNullOrWhiteSpace(components["machineGuid"]))
        {
            throw new InvalidOperationException("The Windows machine identity is unavailable.");
        }

        var canonical = string.Join(
            "\n",
            components.Select(pair => $"{pair.Key}={Normalize(pair.Value)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ReadRegistryValue(
        RegistryHive hive,
        RegistryView view,
        string subKey,
        string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        return key?.GetValue(valueName)?.ToString() ?? string.Empty;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
