using System.Security.AccessControl;
using System.Security.Principal;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public interface ILicenseDirectorySecurity
{
    void EnsureSecureDirectory(string path);
}

public sealed class WindowsLicenseDirectorySecurity : ILicenseDirectorySecurity
{
    public void EnsureSecureDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("License storage ACLs require Windows.");
        }

        Directory.CreateDirectory(path);
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User ??
            throw new InvalidOperationException("The Windows service identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const FileSystemRights rights = FileSystemRights.FullControl;
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            currentSid,
            rights,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            rights,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            rights,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
