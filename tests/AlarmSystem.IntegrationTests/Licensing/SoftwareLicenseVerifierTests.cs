using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Software;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class SoftwareLicenseVerifierTests
{
    private const string MachineId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(365)]
    public void Verify_DemoAcceptsCustomFiniteValidity(int durationDays)
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var verifier = CreateVerifier(licenses);

        var result = verifier.Verify(
            licenses.Create(MachineId, Start, durationDays: durationDays),
            MachineId,
            Start.AddHours(1));

        Assert.True(result.IsValid, result.DiagnosticMessage);
        Assert.Equal(LicenseState.Active, result.Status.State);
        Assert.Equal(2, result.Status.FormatVersion);
        Assert.True(result.Status.SignatureValid);
    }

    [Fact]
    public void Verify_DemoNoneBindingDoesNotRequireMachineMatch()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(MachineId, Start, bindingMode: "NONE"),
            new string('F', 64),
            Start.AddDays(1));

        Assert.True(result.IsValid, result.DiagnosticMessage);
        Assert.Null(result.Status.MachineIdMatches);
        Assert.Equal("NONE", result.Status.BindingMode);
    }

    [Theory]
    [InlineData("SUBSCRIPTION", 365)]
    [InlineData("PERPETUAL", 365)]
    public void Verify_CommercialLicenseRequiresMachineBinding(string type, int durationDays)
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(
                MachineId,
                Start,
                licenseType: type,
                bindingMode: "NONE",
                durationDays: durationDays),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
        Assert.Equal(LicenseState.Invalid, result.Status.State);
    }

    [Fact]
    public void Verify_PerpetualIsActiveWithoutExpiration()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(MachineId, Start, licenseType: "PERPETUAL"),
            MachineId,
            Start.AddYears(25));

        Assert.True(result.IsValid, result.DiagnosticMessage);
        Assert.Null(result.Status.ExpiresAtUtc);
        Assert.Null(result.Status.RemainingDays);
        Assert.Equal("PERPETUAL", result.Status.LicenseType);
    }

    [Fact]
    public void Verify_PerpetualRejectsExpiration()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(
                MachineId,
                Start,
                licenseType: "PERPETUAL",
                forceExpiration: true),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_PerpetualRejectsEmptyMachineIdHash()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(
                MachineId,
                Start,
                licenseType: "PERPETUAL",
                machineIdHashOverride: string.Empty),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_SubscriptionExpiresAndRejectsWrongMachine()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var content = licenses.Create(
            MachineId,
            Start,
            licenseType: "SUBSCRIPTION",
            durationDays: 7);
        var verifier = CreateVerifier(licenses);

        var mismatch = verifier.Verify(content, new string('F', 64), Start.AddDays(1));
        var expired = verifier.Verify(content, MachineId, Start.AddDays(7));

        Assert.Equal(LicenseAuthorizationFailureCodes.MachineMismatch, mismatch.FailureCode);
        Assert.Equal(LicenseState.Expired, expired.Status.State);
    }

    [Fact]
    public void Verify_FutureLicenseIsValidButPending()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(MachineId, Start.AddDays(1)),
            MachineId,
            Start);

        Assert.True(result.IsValid, result.DiagnosticMessage);
        Assert.Equal(LicenseState.Pending, result.Status.State);
        Assert.Equal(LicenseAuthorizationFailureCodes.NotYetActive, result.Status.FailureCode);
    }

    [Fact]
    public void Verify_ExpiresAtExactUtcBoundary()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(MachineId, Start, durationDays: 7),
            MachineId,
            Start.AddDays(7));

        Assert.False(result.IsValid);
        Assert.Equal(LicenseState.Expired, result.Status.State);
    }

    [Fact]
    public void Verify_RejectsWrongMachine()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(MachineId, Start),
            new string('F', 64),
            Start.AddDays(1));

        Assert.False(result.IsValid);
        Assert.Equal(LicenseAuthorizationFailureCodes.MachineMismatch, result.FailureCode);
        Assert.False(result.Status.MachineIdMatches);
    }

    [Fact]
    public void Verify_RejectsTamperedPayload()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var content = licenses.Create(MachineId, Start);
        content[content.Length / 2] = content[content.Length / 2] == (byte)'A' ? (byte)'B' : (byte)'A';

        var result = CreateVerifier(licenses).Verify(content, MachineId, Start.AddDays(1));

        Assert.False(result.IsValid);
        Assert.False(result.Status.SignatureValid);
    }

    [Theory]
    [InlineData("AlarmSystem", 1, "DEMO", "MACHINE", SoftwareLicenseTestFactory.KeyId, "ES256")]
    [InlineData("AnotherProduct", 2, "DEMO", "MACHINE", SoftwareLicenseTestFactory.KeyId, "ES256")]
    [InlineData("AlarmSystem", 2, "OTHER", "MACHINE", SoftwareLicenseTestFactory.KeyId, "ES256")]
    [InlineData("AlarmSystem", 2, "DEMO", "OTHER", SoftwareLicenseTestFactory.KeyId, "ES256")]
    [InlineData("AlarmSystem", 2, "DEMO", "MACHINE", "unknown-key", "ES256")]
    [InlineData("AlarmSystem", 2, "DEMO", "MACHINE", SoftwareLicenseTestFactory.KeyId, "none")]
    public void Verify_RejectsUnsupportedIdentityHeaderOrEnum(
        string productId,
        int version,
        string type,
        string binding,
        string kid,
        string algorithm)
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = CreateVerifier(licenses).Verify(
            licenses.Create(
                MachineId,
                Start,
                productId: productId,
                formatVersion: version,
                licenseType: type,
                bindingMode: binding,
                keyId: kid,
                algorithm: algorithm),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_RejectsDuplicateOrUnknownPayloadProperties()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var common = "\"productId\":\"AlarmSystem\",\"licenseId\":\"L-1\",\"licenseType\":\"DEMO\",\"bindingMode\":\"MACHINE\",\"machineIdHash\":\"" + MachineId + "\",\"issuedAtUtc\":\"2026-10-17T08:00:00Z\",\"notBeforeUtc\":\"2026-10-17T08:00:00Z\",\"expiresAtUtc\":\"2026-11-01T08:00:00Z\",\"features\":[1]";
        var duplicate = licenses.CreateRaw(
            "{\"alg\":\"ES256\",\"kid\":\"runtime-test-key\"}",
            "{\"formatVersion\":2,\"formatVersion\":2," + common + "}");
        var unknown = licenses.CreateRaw(
            "{\"alg\":\"ES256\",\"kid\":\"runtime-test-key\"}",
            "{\"formatVersion\":2," + common + ",\"extra\":true}");

        Assert.False(CreateVerifier(licenses).Verify(duplicate, MachineId, Start).IsValid);
        Assert.False(CreateVerifier(licenses).Verify(unknown, MachineId, Start).IsValid);
    }

    [Fact]
    public void Verify_RejectsInvalidJsonAndUnknownFeature()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var header = "{\"alg\":\"ES256\",\"kid\":\"runtime-test-key\"}";
        var invalidJson = licenses.CreateRaw(header, "{not-json}");
        var unknownFeature = licenses.CreateRaw(
            header,
            "{\"formatVersion\":2,\"productId\":\"AlarmSystem\",\"licenseId\":\"L-1\",\"licenseType\":\"DEMO\",\"bindingMode\":\"NONE\",\"machineIdHash\":null,\"issuedAtUtc\":\"2026-10-17T08:00:00Z\",\"notBeforeUtc\":\"2026-10-17T08:00:00Z\",\"expiresAtUtc\":\"2026-11-01T08:00:00Z\",\"features\":[999]}");

        Assert.False(CreateVerifier(licenses).Verify(invalidJson, MachineId, Start).IsValid);
        Assert.False(CreateVerifier(licenses).Verify(unknownFeature, MachineId, Start).IsValid);
    }

    [Fact]
    public void DefaultTrustStore_TrustsNoRuntimeTestKey()
    {
        using var licenses = new SoftwareLicenseTestFactory();
        var result = new SoftwareLicenseVerifier(new SoftwareLicenseTrustStore()).Verify(
            licenses.Create(MachineId, Start),
            MachineId,
            Start.AddDays(1));

        Assert.False(result.IsValid);
        Assert.Equal(LicenseAuthorizationFailureCodes.SignatureInvalid, result.FailureCode);
    }

    private static SoftwareLicenseVerifier CreateVerifier(SoftwareLicenseTestFactory licenses) =>
        new(new SoftwareLicenseTrustStore(licenses.PublicKeys));
}
