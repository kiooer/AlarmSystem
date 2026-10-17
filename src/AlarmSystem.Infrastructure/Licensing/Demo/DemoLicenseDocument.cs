using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed record DemoLicenseDocument(
    int FormatVersion,
    string ProductId,
    string LicenseId,
    string LicenseType,
    string MachineIdHash,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<NotificationFeature> Features,
    string KeyId);
