using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Demo;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class DemoLicenseVerifierTests
{
    private const string MachineId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    public void Verify_AcceptsValidSupportedDuration(int durationDays)
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));

        var result = verifier.Verify(
            licenses.Create(MachineId, Start, durationDays),
            MachineId,
            Start.AddDays(1));

        Assert.True(result.IsValid);
        Assert.Equal(LicenseState.Active, result.Status.State);
        Assert.Equal(durationDays - 1, result.Status.RemainingDays);
        Assert.Equal(Enum.GetValues<NotificationFeature>(), result.Status.AuthorizedFeatures);
    }

    [Fact]
    public void Verify_ExpiresAtExactUtcBoundary()
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));

        var result = verifier.Verify(
            licenses.Create(MachineId, Start),
            MachineId,
            Start.AddDays(15));

        Assert.False(result.IsValid);
        Assert.Equal(LicenseState.Expired, result.Status.State);
        Assert.Equal(LicenseAuthorizationFailureCodes.Expired, result.FailureCode);
    }

    [Fact]
    public void Verify_RejectsTamperedPayload()
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));
        var content = licenses.Create(MachineId, Start);
        content[content.Length / 2] = content[content.Length / 2] == (byte)'A' ? (byte)'B' : (byte)'A';

        var result = verifier.Verify(content, MachineId, Start.AddDays(1));

        Assert.False(result.IsValid);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.FailureCode);
    }

    [Theory]
    [InlineData("AnotherProduct", "DEMO", DemoLicenseTestFactory.KeyId, "ES256")]
    [InlineData("AlarmSystem", "OTHER", DemoLicenseTestFactory.KeyId, "ES256")]
    [InlineData("AlarmSystem", "DEMO", "unknown-key", "ES256")]
    [InlineData("AlarmSystem", "DEMO", DemoLicenseTestFactory.KeyId, "none")]
    public void Verify_RejectsInvalidIdentityOrHeader(
        string productId,
        string licenseType,
        string keyId,
        string algorithm)
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));

        var result = verifier.Verify(
            licenses.Create(
                MachineId,
                Start,
                productId: productId,
                licenseType: licenseType,
                keyId: keyId,
                algorithm: algorithm),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_RejectsWrongMachine()
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));

        var result = verifier.Verify(
            licenses.Create(MachineId, Start),
            new string('F', 64),
            Start.AddDays(1));

        Assert.False(result.IsValid);
        Assert.False(result.Status.MachineIdMatches);
    }

    [Fact]
    public void Verify_RejectsDuplicateHeaderField()
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));
        var payload = """
            {"formatVersion":1,"productId":"AlarmSystem","licenseId":"DEMO-1","licenseType":"DEMO","machineIdHash":"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF","issuedAtUtc":"2026-10-17T08:00:00Z","notBeforeUtc":"2026-10-17T08:00:00Z","expiresAtUtc":"2026-11-01T08:00:00Z","features":[2]}
            """;

        var result = verifier.Verify(
            licenses.CreateRaw(
                "{\"alg\":\"ES256\",\"alg\":\"ES256\",\"kid\":\"test-key-1\"}",
                payload),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_RejectsUnknownPayloadField()
    {
        using var licenses = new DemoLicenseTestFactory();
        var verifier = new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys));
        var payload = """
            {"formatVersion":1,"productId":"AlarmSystem","licenseId":"DEMO-1","licenseType":"DEMO","machineIdHash":"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF","issuedAtUtc":"2026-10-17T08:00:00Z","notBeforeUtc":"2026-10-17T08:00:00Z","expiresAtUtc":"2026-11-01T08:00:00Z","features":[2],"extra":true}
            """;

        var result = verifier.Verify(
            licenses.CreateRaw(
                "{\"alg\":\"ES256\",\"kid\":\"test-key-1\"}",
                payload),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
    }
}
