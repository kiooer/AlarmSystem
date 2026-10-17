using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;

namespace AlarmSystem.Desktop.ViewModels;

public sealed class LicenseFeatureStatusRow
{
    public required string Module { get; init; }

    public required string Licensed { get; init; }

    public required string Source { get; init; }
}

public sealed class LicenseTestViewModel : INotifyPropertyChanged
{
    private readonly ILicenseTestService service;
    private readonly ILicenseFilePicker filePicker;
    private readonly ILicenseImportConfirmationService confirmationService;
    private readonly IClipboardService clipboardService;
    private string machineId = Strings.NotAvailable;
    private string selectedLicensePath = string.Empty;
    private string licenseId = Strings.NotAvailable;
    private string licenseType = Strings.LicenseTypeUnlicensed;
    private string formatVersion = Strings.NotAvailable;
    private string licenseSource = Strings.LicenseSourceNone;
    private string bindingMode = Strings.NotAvailable;
    private string signatureValidation = Strings.NotAvailable;
    private string status = Strings.LicenseStatusUnlicensed;
    private string issuedAt = Strings.NotAvailable;
    private string validFrom = Strings.NotAvailable;
    private string expiresAt = Strings.NotAvailable;
    private string remainingDays = Strings.NotAvailable;
    private string machineIdMatch = Strings.NotAvailable;
    private string authorizedFeatures = Strings.NotAvailable;
    private string diagnosticMessage = Strings.LicenseStatusReady;
    private string errorCode = Strings.NotAvailable;
    private string validatedAt = Strings.NotAvailable;
    private string serviceConnection = Strings.LicenseServiceNotConnected;
    private bool previewIsValid;
    private bool serviceConnected;

    public LicenseTestViewModel(
        ILicenseTestService service,
        ILicenseFilePicker filePicker,
        ILicenseImportConfirmationService confirmationService,
        IClipboardService clipboardService)
    {
        this.service = service;
        this.filePicker = filePicker;
        this.confirmationService = confirmationService;
        this.clipboardService = clipboardService;
        CopyMachineIdCommand = new RelayCommand(CopyMachineId, () => MachineId != Strings.NotAvailable);
        ReloadMachineIdCommand = new RelayCommand(ReloadMachineId);
        SelectLicenseCommand = new RelayCommand(SelectLicense);
        ValidateLicenseCommand = new AsyncRelayCommand(
            ValidateLicenseAsync,
            () => !string.IsNullOrWhiteSpace(SelectedLicensePath));
        ImportLicenseCommand = new AsyncRelayCommand(
            ImportLicenseAsync,
            () => previewIsValid && serviceConnected);
        RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);
        RecheckFeaturesCommand = new AsyncRelayCommand(RefreshStatusAsync);
        ReloadMachineId();
        UpdateFeatureRows(null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand CopyMachineIdCommand { get; }
    public RelayCommand ReloadMachineIdCommand { get; }
    public RelayCommand SelectLicenseCommand { get; }
    public AsyncRelayCommand ValidateLicenseCommand { get; }
    public AsyncRelayCommand ImportLicenseCommand { get; }
    public AsyncRelayCommand RefreshStatusCommand { get; }
    public AsyncRelayCommand RecheckFeaturesCommand { get; }

    public ObservableCollection<LicenseFeatureStatusRow> FeatureRows { get; } = [];

    public string MachineId { get => machineId; private set => SetField(ref machineId, value); }
    public string SelectedLicensePath { get => selectedLicensePath; private set => SetField(ref selectedLicensePath, value); }
    public string LicenseId { get => licenseId; private set => SetField(ref licenseId, value); }
    public string LicenseType { get => licenseType; private set => SetField(ref licenseType, value); }
    public string FormatVersion { get => formatVersion; private set => SetField(ref formatVersion, value); }
    public string LicenseSourceDisplay { get => licenseSource; private set => SetField(ref licenseSource, value); }
    public string BindingMode { get => bindingMode; private set => SetField(ref bindingMode, value); }
    public string SignatureValidation { get => signatureValidation; private set => SetField(ref signatureValidation, value); }
    public string Status { get => status; private set => SetField(ref status, value); }
    public string IssuedAt { get => issuedAt; private set => SetField(ref issuedAt, value); }
    public string ValidFrom { get => validFrom; private set => SetField(ref validFrom, value); }
    public string ExpiresAt { get => expiresAt; private set => SetField(ref expiresAt, value); }
    public string RemainingDays { get => remainingDays; private set => SetField(ref remainingDays, value); }
    public string MachineIdMatch { get => machineIdMatch; private set => SetField(ref machineIdMatch, value); }
    public string AuthorizedFeatures { get => authorizedFeatures; private set => SetField(ref authorizedFeatures, value); }
    public string DiagnosticMessage { get => diagnosticMessage; private set => SetField(ref diagnosticMessage, value); }
    public string ErrorCode { get => errorCode; private set => SetField(ref errorCode, value); }
    public string ValidatedAt { get => validatedAt; private set => SetField(ref validatedAt, value); }
    public string ServiceConnection { get => serviceConnection; private set => SetField(ref serviceConnection, value); }

    public async Task InitializeAsync() => await RefreshStatusAsync();

    private void ReloadMachineId()
    {
        try
        {
            MachineId = service.GetMachineId();
            DiagnosticMessage = Strings.LicenseMachineIdLoaded;
        }
        catch
        {
            MachineId = Strings.NotAvailable;
            DiagnosticMessage = Strings.LicenseMachineIdUnavailable;
        }

        CopyMachineIdCommand.RaiseCanExecuteChanged();
    }

    private void CopyMachineId()
    {
        try
        {
            clipboardService.SetText(MachineId);
            DiagnosticMessage = Strings.LicenseMachineIdCopied;
        }
        catch
        {
            DiagnosticMessage = Strings.LicenseClipboardFailed;
        }
    }

    private void SelectLicense()
    {
        var path = filePicker.SelectLicenseFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedLicensePath = path;
        previewIsValid = false;
        DiagnosticMessage = Strings.LicenseFileSelected;
        ValidateLicenseCommand.RaiseCanExecuteChanged();
        ImportLicenseCommand.RaiseCanExecuteChanged();
    }

    private async Task ValidateLicenseAsync()
    {
        try
        {
            var result = await service.PreviewAsync(SelectedLicensePath, CancellationToken.None);
            previewIsValid = result.Validation?.IsValid == true;
            ApplyResult(result);
        }
        catch
        {
            previewIsValid = false;
            DiagnosticMessage = Strings.LicenseFileReadFailed;
            ErrorCode = LicenseAuthorizationFailureCodes.ValidationFailed;
        }

        ImportLicenseCommand.RaiseCanExecuteChanged();
    }

    private async Task ImportLicenseAsync()
    {
        var currentStatus = CurrentStatus;
        if (currentStatus is null || !confirmationService.ConfirmImport(currentStatus))
        {
            DiagnosticMessage = Strings.LicenseImportCancelled;
            return;
        }

        try
        {
            var result = await service.ImportAsync(SelectedLicensePath, CancellationToken.None);
            ApplyResult(result);
            previewIsValid = result.Succeeded;
        }
        catch
        {
            DiagnosticMessage = Strings.LicenseImportFailed;
            ErrorCode = LicenseAuthorizationFailureCodes.ValidationFailed;
        }

        ImportLicenseCommand.RaiseCanExecuteChanged();
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            ApplyResult(await service.RefreshAsync(CancellationToken.None));
        }
        catch
        {
            serviceConnected = false;
            ServiceConnection = Strings.LicenseServiceNotConnected;
            DiagnosticMessage = Strings.LicenseRefreshFailed;
        }

        ImportLicenseCommand.RaiseCanExecuteChanged();
    }

    private LicenseStatus? CurrentStatus { get; set; }

    private void ApplyResult(LicenseTestOperationResult result)
    {
        serviceConnected = result.ServiceConnected;
        ServiceConnection = serviceConnected
            ? Strings.LicenseServiceConnected
            : Strings.LicenseServiceNotConnected;
        DiagnosticMessage = LocalizeDiagnostic(result);
        ErrorCode = result.ErrorCode ?? Strings.NotAvailable;
        if (result.Status is not null)
        {
            ApplyStatus(result.Status);
        }
    }

    private void ApplyStatus(LicenseStatus value)
    {
        CurrentStatus = value;
        LicenseId = value.LicenseId ?? Strings.NotAvailable;
        LicenseType = LocalizeLicenseType(value);
        FormatVersion = value.FormatVersion?.ToString() ?? Strings.NotAvailable;
        LicenseSourceDisplay = LocalizeSource(value.Source);
        BindingMode = value.BindingMode switch
        {
            "MACHINE" => Strings.LicenseBindingMachine,
            "NONE" => Strings.LicenseBindingNone,
            _ => Strings.NotAvailable,
        };
        SignatureValidation = value.SignatureValid switch
        {
            true => Strings.LicenseSignatureValid,
            false => Strings.LicenseSignatureInvalid,
            _ => Strings.NotAvailable,
        };
        Status = value.State switch
        {
            LicenseState.Active => Strings.LicenseStatusActive,
            LicenseState.Pending => Strings.LicenseStatusPending,
            LicenseState.Expired => Strings.LicenseStatusExpired,
            LicenseState.Invalid => Strings.LicenseStatusInvalid,
            LicenseState.TimeAnomaly => Strings.LicenseStatusTimeAnomaly,
            _ => Strings.LicenseStatusUnlicensed,
        };
        IssuedAt = FormatDate(value.IssuedAtUtc);
        ValidFrom = FormatDate(value.NotBeforeUtc);
        var isPerpetual = string.Equals(value.LicenseType, "PERPETUAL", StringComparison.Ordinal);
        ExpiresAt = isPerpetual ? Strings.LicensePerpetual : FormatDate(value.ExpiresAtUtc);
        RemainingDays = isPerpetual
            ? Strings.NotApplicable
            : value.RemainingDays?.ToString() ?? Strings.NotAvailable;
        MachineIdMatch = string.Equals(value.BindingMode, "NONE", StringComparison.Ordinal)
            ? Strings.LicenseBindingNotRequired
            : value.MachineIdMatches switch
            {
                true => Strings.Yes,
                false => Strings.No,
                _ => Strings.NotAvailable,
            };
        AuthorizedFeatures = value.AuthorizedFeatures.Count == 0
            ? Strings.NotAvailable
            : string.Join(", ", value.AuthorizedFeatures);
        ValidatedAt = FormatDate(value.ValidatedAtUtc);
        ErrorCode = value.FailureCode ?? ErrorCode;
        DiagnosticMessage = value.RemainingDays is 7 or 3 or 1
            ? string.Format(Strings.LicenseExpiryReminderFormat, value.RemainingDays)
            : DiagnosticMessage;
        UpdateFeatureRows(value);
    }

    private void UpdateFeatureRows(LicenseStatus? value)
    {
        FeatureRows.Clear();
        foreach (var feature in Enum.GetValues<NotificationFeature>())
        {
            var licensed = value?.State == LicenseState.Active &&
                value.AuthorizedFeatures.Contains(feature);
            var source = licensed && value!.FeatureSources?.TryGetValue(feature, out var featureSource) == true
                ? featureSource
                : licensed
                    ? value!.Source
                    : LicenseSource.None;
            FeatureRows.Add(new LicenseFeatureStatusRow
            {
                Module = feature.ToString(),
                Licensed = licensed ? Strings.Yes : Strings.No,
                Source = source switch
                {
                    LicenseSource.Demo => Strings.LicenseTypeDemo,
                    LicenseSource.Software => Strings.LicenseSourceSoftware,
                    LicenseSource.Dongle => Strings.LicenseTypeDongle,
                    LicenseSource.Composite => Strings.LicenseTypeComposite,
                    _ => Strings.LicenseSourceNone,
                },
            });
        }
    }

    private static string LocalizeDiagnostic(LicenseTestOperationResult result)
    {
        if (!result.ServiceConnected)
        {
            return Strings.LicenseServiceUnavailableDiagnostic;
        }

        return result.Status?.State switch
        {
            LicenseState.Active => Strings.LicenseValidationSucceeded,
            LicenseState.Pending => Strings.LicenseValidationPending,
            LicenseState.Expired => Strings.LicenseValidationExpired,
            LicenseState.TimeAnomaly => Strings.LicenseValidationTimeAnomaly,
            LicenseState.Invalid => Strings.LicenseValidationInvalid,
            _ => result.ErrorCode == LicenseAuthorizationFailureCodes.FeatureNotLicensed
                ? Strings.LicenseValidationFeatureNotLicensed
                : Strings.LicenseValidationUnavailable,
        };
    }

    private static string LocalizeLicenseType(LicenseStatus status) => status.LicenseType switch
    {
        "DEMO" or "Demo" => Strings.LicenseTypeDemo,
        "SUBSCRIPTION" => Strings.LicenseTypeSubscription,
        "PERPETUAL" => Strings.LicenseTypePerpetual,
        "Dongle" => Strings.LicenseTypeDongle,
        "Unlicensed" => Strings.LicenseTypeUnlicensed,
        _ when status.Source == LicenseSource.Composite => status.LicenseType,
        _ => status.LicenseType,
    };

    private static string LocalizeSource(LicenseSource source) => source switch
    {
        LicenseSource.Demo => Strings.LicenseSourceDemoV1,
        LicenseSource.Software => Strings.LicenseSourceSoftware,
        LicenseSource.Dongle => Strings.LicenseTypeDongle,
        LicenseSource.Composite => Strings.LicenseTypeComposite,
        _ => Strings.LicenseSourceNone,
    };

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? Strings.NotAvailable;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
