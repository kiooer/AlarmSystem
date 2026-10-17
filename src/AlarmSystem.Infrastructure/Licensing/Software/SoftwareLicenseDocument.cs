using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Software;

public enum SoftwareLicenseType
{
    Demo = 1,
    Subscription = 2,
    Perpetual = 3,
}

public enum SoftwareLicenseBindingMode
{
    Machine = 1,
    None = 2,
}

public sealed record SoftwareLicenseDocument(
    int FormatVersion,
    string ProductId,
    string LicenseId,
    SoftwareLicenseType LicenseType,
    SoftwareLicenseBindingMode BindingMode,
    string? MachineIdHash,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc,
    IReadOnlyList<NotificationFeature> Features,
    string KeyId);
