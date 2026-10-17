namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed record DemoLicenseState(string LicenseId, DateTimeOffset LastSeenUtc);

public interface ILicenseStateProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}
