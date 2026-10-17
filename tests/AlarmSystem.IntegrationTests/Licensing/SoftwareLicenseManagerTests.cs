using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Demo;
using AlarmSystem.Infrastructure.Licensing.Software;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class SoftwareLicenseManagerTests
{
    private const string MachineId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImportAndRestart_PreservesActiveSubscription()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);

        var imported = await manager.ImportAsync(
            licenses.Create(MachineId, Start, licenseType: "SUBSCRIPTION", durationDays: 365),
            CancellationToken.None);
        var restarted = environment.CreateManager(licenses);
        var status = await restarted.ReadStatusAsync(CancellationToken.None);

        Assert.True(imported.IsValid, imported.DiagnosticMessage);
        Assert.Equal(LicenseState.Active, status.State);
        Assert.Equal("SUBSCRIPTION", status.LicenseType);
        Assert.True(File.Exists(environment.Options.LicensePath));
        Assert.True(File.Exists(environment.Options.StatePath));
    }

    [Fact]
    public async Task ImportAndRestart_PreservesPerpetualLicense()
    {
        using var environment = new TestEnvironment(Start.AddYears(20));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        var content = licenses.Create(
            MachineId,
            Start,
            licenseType: "PERPETUAL",
            licenseId: "PERPETUAL-1");

        var imported = await manager.ImportAsync(content, CancellationToken.None);
        var status = await environment.CreateManager(licenses).ReadStatusAsync(CancellationToken.None);

        Assert.True(imported.IsValid, imported.DiagnosticMessage);
        Assert.Equal(LicenseState.Active, status.State);
        Assert.Null(status.ExpiresAtUtc);
    }

    [Fact]
    public async Task ReimportSameDemo_DoesNotExtendDeadlineAndSurvivesRestart()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new SoftwareLicenseTestFactory();
        var content = licenses.Create(
            MachineId,
            Start,
            licenseType: "DEMO",
            durationDays: 7,
            licenseId: "DEMO-V2-1");
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(content, CancellationToken.None);
        environment.Time.Set(Start.AddDays(6));

        var reimported = await manager.ImportAsync(content, CancellationToken.None);
        var restarted = await environment.CreateManager(licenses).ReadStatusAsync(CancellationToken.None);

        Assert.True(reimported.IsValid, reimported.DiagnosticMessage);
        Assert.Equal(Start.AddDays(7), restarted.ExpiresAtUtc);
        Assert.Equal(1, restarted.RemainingDays);
    }

    [Fact]
    public async Task ImportDifferentLicenseId_ReplacesExpiredLicenseWithoutResettingClock()
    {
        using var environment = new TestEnvironment(Start.AddDays(10));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(
            licenses.Create(
                MachineId,
                Start,
                licenseType: "SUBSCRIPTION",
                durationDays: 30,
                licenseId: "OLD-1"),
            CancellationToken.None);
        environment.Time.Set(Start.AddDays(31));

        var replacement = await manager.ImportAsync(
            licenses.Create(
                MachineId,
                Start.AddDays(30),
                licenseType: "SUBSCRIPTION",
                durationDays: 365,
                licenseId: "NEW-1"),
            CancellationToken.None);
        var status = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.True(replacement.IsValid, replacement.DiagnosticMessage);
        Assert.Equal("NEW-1", status.LicenseId);
        environment.Time.Set(Start.AddDays(1));
        Assert.Equal(
            LicenseState.TimeAnomaly,
            (await manager.ReadStatusAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task ImportSameLicenseIdWithDifferentSignedContent_IsRejected()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(
            licenses.Create(MachineId, Start, licenseId: "SAME-1", durationDays: 45),
            CancellationToken.None);

        var result = await manager.ImportAsync(
            licenses.Create(MachineId, Start, licenseId: "SAME-1", durationDays: 90),
            CancellationToken.None);
        var status = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseAuthorizationFailureCodes.StateInvalid, result.FailureCode);
        Assert.Equal("SAME-1", status.LicenseId);
        Assert.Equal(Start.AddDays(45), status.ExpiresAtUtc);
    }

    [Fact]
    public async Task InvalidImport_PreservesPreviouslyActiveLicense()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(licenses.Create(MachineId, Start), CancellationToken.None);
        var result = await manager.ImportAsync("not-a-license"u8.ToArray(), CancellationToken.None);
        var status = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseState.Active, status.State);
    }

    [Fact]
    public async Task ReadStatus_FailsClosedForMissingCorruptOrMismatchedState()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(licenses.Create(MachineId, Start), CancellationToken.None);
        var validState = await File.ReadAllBytesAsync(environment.Options.StatePath);

        File.Delete(environment.Options.StatePath);
        Assert.Equal(
            LicenseState.Invalid,
            (await manager.ReadStatusAsync(CancellationToken.None)).State);

        await File.WriteAllTextAsync(environment.Options.StatePath, "corrupt");
        Assert.Equal(
            LicenseState.Invalid,
            (await manager.ReadStatusAsync(CancellationToken.None)).State);

        await File.WriteAllBytesAsync(environment.Options.StatePath, validState);
        await File.WriteAllTextAsync(
            environment.Options.StatePath,
            "{\"licenseId\":\"OTHER\",\"licenseFingerprint\":\"00\",\"lastSeenUtc\":\"2026-10-17T08:00:00Z\"}");
        Assert.Equal(
            LicenseState.Invalid,
            (await manager.ReadStatusAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task PreviewRejectsOversizedLicenseWithoutWritingFiles()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new SoftwareLicenseTestFactory();
        var manager = environment.CreateManager(licenses);

        var result = await manager.PreviewImportAsync(
            new byte[SoftwareLicenseOptions.DefaultMaximumLicenseBytes + 1],
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.False(File.Exists(environment.Options.LicensePath));
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"AlarmSystem-SoftwareLicenseTests-{Guid.NewGuid():N}");

        public TestEnvironment(DateTimeOffset now)
        {
            Options = new SoftwareLicenseOptions
            {
                LicensePath = Path.Combine(root, "software-v2.lic"),
                StatePath = Path.Combine(root, "software-license-state.dat"),
            };
            Time = new MutableTimeProvider(now);
        }

        public SoftwareLicenseOptions Options { get; }

        public MutableTimeProvider Time { get; }

        public SoftwareLicenseManager CreateManager(SoftwareLicenseTestFactory licenses)
        {
            var store = new SoftwareLicenseFileStore(
                Options,
                new PassthroughProtector(),
                new TestDirectorySecurity());
            return new SoftwareLicenseManager(
                Options,
                new SoftwareLicenseVerifier(new SoftwareLicenseTrustStore(licenses.PublicKeys)),
                store,
                new StubFingerprintProvider(),
                new SoftwareLicenseTimeGuard(Options, store),
                Time);
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Set(DateTimeOffset value) => current = value;
    }

    private sealed class StubFingerprintProvider : IMachineFingerprintProvider
    {
        public string GetMachineId() => MachineId;
    }

    private sealed class PassthroughProtector : ISoftwareLicenseStateProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => protectedData.ToArray();
    }

    private sealed class TestDirectorySecurity : ILicenseDirectorySecurity
    {
        public void EnsureSecureDirectory(string path) => Directory.CreateDirectory(path);
    }
}
