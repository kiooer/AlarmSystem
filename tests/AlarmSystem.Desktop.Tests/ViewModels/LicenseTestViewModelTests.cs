using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;

namespace AlarmSystem.Desktop.Tests.ViewModels;

public sealed class LicenseTestViewModelTests
{
    [Fact]
    public async Task InitializeAsync_DisplaysActiveDemoAndFeaturePermissions()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var status = new LicenseStatus(
            LicenseSource.Demo,
            LicenseState.Active,
            "DEMO-TEST-1",
            "Demo",
            now,
            now,
            now.AddDays(15),
            15,
            true,
            [NotificationFeature.Teams, NotificationFeature.Line],
            now,
            null,
            "active");
        var viewModel = CreateViewModel(new StubLicenseTestService
        {
            Result = new LicenseTestOperationResult(true, true, status, null, null, "active"),
        });

        await viewModel.InitializeAsync();

        Assert.Equal("DEMO-TEST-1", viewModel.LicenseId);
        Assert.Equal(Strings.LicenseTypeDemo, viewModel.LicenseType);
        Assert.Equal(Strings.LicenseStatusActive, viewModel.Status);
        Assert.Equal(Strings.LicenseServiceConnected, viewModel.ServiceConnection);
        Assert.Equal(3, viewModel.FeatureRows.Count);
        Assert.Equal(Strings.Yes, viewModel.FeatureRows.Single(row => row.Module == "Teams").Licensed);
        Assert.Equal(Strings.No, viewModel.FeatureRows.Single(row => row.Module == "Email").Licensed);
    }

    [Fact]
    public async Task InitializeAsync_WhenWorkerUnavailable_DoesNotClaimActivation()
    {
        var viewModel = CreateViewModel(new StubLicenseTestService
        {
            Result = new LicenseTestOperationResult(
                false,
                false,
                null,
                null,
                "license_service_unavailable",
                "not connected"),
        });

        await viewModel.InitializeAsync();

        Assert.Equal(Strings.LicenseServiceNotConnected, viewModel.ServiceConnection);
        Assert.Equal(Strings.LicenseStatusUnlicensed, viewModel.Status);
        Assert.All(viewModel.FeatureRows, row => Assert.Equal(Strings.No, row.Licensed));
    }

    [Fact]
    public async Task InitializeAsync_DisplaysPerpetualSoftwareLicenseMetadata()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var status = new LicenseStatus(
            LicenseSource.Software,
            LicenseState.Active,
            "PERPETUAL-1",
            "PERPETUAL",
            now,
            now,
            null,
            null,
            true,
            [NotificationFeature.Email],
            now,
            null,
            "active",
            FormatVersion: 2,
            BindingMode: "MACHINE",
            SignatureValid: true);
        var viewModel = CreateViewModel(new StubLicenseTestService
        {
            Result = new LicenseTestOperationResult(true, true, status, null, null, "active"),
        });

        await viewModel.InitializeAsync();

        Assert.Equal(Strings.LicenseTypePerpetual, viewModel.LicenseType);
        Assert.Equal("2", viewModel.FormatVersion);
        Assert.Equal(Strings.LicenseSourceSoftware, viewModel.LicenseSourceDisplay);
        Assert.Equal(Strings.LicenseBindingMachine, viewModel.BindingMode);
        Assert.Equal(Strings.LicenseSignatureValid, viewModel.SignatureValidation);
        Assert.Equal(Strings.LicensePerpetual, viewModel.ExpiresAt);
        Assert.Equal(Strings.NotApplicable, viewModel.RemainingDays);
    }

    [Fact]
    public async Task InitializeAsync_DisplaysNoBindingAsNotRequired()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var status = new LicenseStatus(
            LicenseSource.Software,
            LicenseState.Active,
            "DEMO-V2-1",
            "DEMO",
            now,
            now,
            now.AddDays(7),
            7,
            null,
            [NotificationFeature.Teams],
            now,
            null,
            "active",
            FormatVersion: 2,
            BindingMode: "NONE",
            SignatureValid: true);
        var viewModel = CreateViewModel(new StubLicenseTestService
        {
            Result = new LicenseTestOperationResult(true, true, status, null, null, "active"),
        });

        await viewModel.InitializeAsync();

        Assert.Equal(Strings.LicenseBindingNone, viewModel.BindingMode);
        Assert.Equal(Strings.LicenseBindingNotRequired, viewModel.MachineIdMatch);
    }

    private static LicenseTestViewModel CreateViewModel(ILicenseTestService service) => new(
        service,
        new StubFilePicker(),
        new StubConfirmation(),
        new StubClipboard());

    private sealed class StubLicenseTestService : ILicenseTestService
    {
        public required LicenseTestOperationResult Result { get; init; }

        public string GetMachineId() => new string('A', 64);

        public Task<LicenseTestOperationResult> ReadStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result);

        public Task<LicenseTestOperationResult> PreviewAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<LicenseTestOperationResult> ImportAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<LicenseTestOperationResult> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }

    private sealed class StubFilePicker : ILicenseFilePicker
    {
        public string? SelectLicenseFile() => null;
    }

    private sealed class StubConfirmation : ILicenseImportConfirmationService
    {
        public bool ConfirmImport(LicenseStatus status) => false;
    }

    private sealed class StubClipboard : IClipboardService
    {
        public void SetText(string value)
        {
        }
    }
}
