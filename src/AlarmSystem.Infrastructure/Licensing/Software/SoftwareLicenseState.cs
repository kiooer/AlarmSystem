namespace AlarmSystem.Infrastructure.Licensing.Software;

public sealed record SoftwareLicenseState(
    string LicenseId,
    string LicenseFingerprint,
    DateTimeOffset LastSeenUtc);

public interface ISoftwareLicenseStateProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}
