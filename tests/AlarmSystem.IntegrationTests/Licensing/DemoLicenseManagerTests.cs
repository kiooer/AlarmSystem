using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Licensing.Demo;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class DemoLicenseManagerTests
{
    private const string MachineId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImportAsync_RevalidatesAndPersistsState()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);

        var result = await manager.ImportAsync(
            licenses.Create(MachineId, Start, 15),
            CancellationToken.None);
        var reloaded = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.True(result.IsValid, result.DiagnosticMessage);
        Assert.Equal(LicenseState.Active, reloaded.State);
        Assert.True(File.Exists(environment.Options.LicensePath));
        Assert.True(File.Exists(environment.Options.StatePath));
    }

    [Fact]
    public async Task InvalidImport_PreservesPreviouslyActiveLicense()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(licenses.Create(MachineId, Start), CancellationToken.None);

        var result = await manager.ImportAsync("not-a-license"u8.ToArray(), CancellationToken.None);
        var status = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseState.Active, status.State);
        Assert.Equal("DEMO-TEST-0001", status.LicenseId);
    }

    [Fact]
    public async Task ReadStatusAsync_FailsClosedWhenStateIsMissingOrCorrupt()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(licenses.Create(MachineId, Start), CancellationToken.None);

        File.Delete(environment.Options.StatePath);
        var missing = await manager.ReadStatusAsync(CancellationToken.None);
        await File.WriteAllTextAsync(environment.Options.StatePath, "corrupt");
        var corrupt = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Invalid, missing.State);
        Assert.Equal(LicenseState.Invalid, corrupt.State);
    }

    [Fact]
    public async Task ReadStatusAsync_DetectsClockRollbackBeyondTolerance()
    {
        using var environment = new TestEnvironment(Start.AddDays(2));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(licenses.Create(MachineId, Start), CancellationToken.None);
        environment.Time.Set(Start.AddDays(1));

        var status = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.TimeAnomaly, status.State);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, status.FailureCode);
    }

    [Fact]
    public async Task ReimportSameLicense_DoesNotResetClockRollbackState()
    {
        using var environment = new TestEnvironment(Start.AddDays(10));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        var content = licenses.Create(MachineId, Start);
        await manager.ImportAsync(content, CancellationToken.None);
        environment.Time.Set(Start.AddDays(1));

        var reimport = await manager.ImportAsync(content, CancellationToken.None);
        var status = await manager.ReadStatusAsync(CancellationToken.None);

        Assert.False(reimport.IsValid);
        Assert.Equal(LicenseState.TimeAnomaly, reimport.Status.State);
        Assert.Equal(LicenseState.TimeAnomaly, status.State);
    }

    [Fact]
    public async Task AuthorizeAsync_DeniesFeatureNotPresent()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(
            licenses.Create(MachineId, Start, features: [NotificationFeature.Teams]),
            CancellationToken.None);

        var result = await manager.AuthorizeAsync(
            NotificationFeature.Email,
            CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal(LicenseAuthorizationFailureCodes.FeatureNotLicensed, result.FailureCode);
    }

    [Fact]
    public async Task ExpiredDemo_PreventsNotificationTransportCall()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);
        await manager.ImportAsync(licenses.Create(MachineId, Start), CancellationToken.None);
        environment.Time.Set(Start.AddDays(15));
        var inner = new RecordingChannel();
        var channel = new LicensedNotificationChannel(
            inner,
            manager,
            NotificationFeature.Teams);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendStatus.PermanentFailure, result.Status);
        Assert.Equal(LicenseAuthorizationFailureCodes.Expired, result.ErrorCode);
        Assert.Equal(0, inner.SendCount);
    }

    [Fact]
    public async Task PreviewImport_RejectsOversizedLicenseWithoutWritingFiles()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);

        var result = await manager.PreviewImportAsync(
            new byte[DemoLicenseOptions.DefaultMaximumLicenseBytes + 1],
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.False(File.Exists(environment.Options.LicensePath));
    }

    [Fact]
    public async Task AuthorizeAsync_WithoutInstalledDemo_PreservesUnavailableCode()
    {
        using var environment = new TestEnvironment(Start.AddDays(1));
        using var licenses = new DemoLicenseTestFactory();
        var manager = environment.CreateManager(licenses);

        var result = await manager.AuthorizeAsync(
            NotificationFeature.Teams,
            CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal(LicenseAuthorizationFailureCodes.DongleUnavailable, result.FailureCode);
    }

    private static AlarmNotification CreateNotification() => new(
        Guid.NewGuid(),
        "TAG-1",
        "Area",
        AlarmEventType.Alarm,
        AlarmSeverity.High,
        "message",
        Start,
        "Equipment");

    private sealed class TestEnvironment : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"AlarmSystem-LicenseTests-{Guid.NewGuid():N}");

        public TestEnvironment(DateTimeOffset now)
        {
            Options = new DemoLicenseOptions
            {
                LicensePath = Path.Combine(root, "demo.lic"),
                StatePath = Path.Combine(root, "license-state.dat"),
            };
            Time = new MutableTimeProvider(now);
        }

        public DemoLicenseOptions Options { get; }

        public MutableTimeProvider Time { get; }

        public DemoLicenseManager CreateManager(DemoLicenseTestFactory licenses)
        {
            var store = new DemoLicenseFileStore(
                Options,
                new PassthroughProtector(),
                new TestDirectorySecurity());
            return new DemoLicenseManager(
                Options,
                new DemoLicenseVerifier(new DemoLicenseTrustStore(licenses.PublicKeys)),
                store,
                new StubFingerprintProvider(),
                new LicenseTimeGuard(Options, store),
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

    private sealed class PassthroughProtector : ILicenseStateProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => protectedData.ToArray();
    }

    private sealed class TestDirectorySecurity : ILicenseDirectorySecurity
    {
        public void EnsureSecureDirectory(string path) => Directory.CreateDirectory(path);
    }

    private sealed class RecordingChannel : INotificationChannel
    {
        public int SendCount { get; private set; }

        public string ChannelName => "Recording";

        public string DestinationReference => "test";

        public Task<NotificationSendResult> SendAsync(
            AlarmNotification notification,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(NotificationSendResult.Success());
        }
    }
}
