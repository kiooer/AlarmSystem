using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.Desktop.ViewModels;

public sealed class EmailNotificationTestViewModel : INotifyPropertyChanged
{
    private const string SourceTimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private readonly EmailTemplateProvider templateProvider;
    private readonly IEmailNotificationTestService testService;
    private readonly IEmailSendConfirmationService confirmationService;
    private readonly TimeProvider timeProvider;
    private readonly RelayCommand newTestDataCommand;
    private readonly RelayCommand updatePreviewCommand;
    private readonly RelayCommand cancelCommand;

    private string smtpHost = string.Empty;
    private string smtpPortText = "587";
    private EmailSecurityMode selectedSecurityMode = EmailSecurityMode.StartTls;
    private bool allowUnencryptedInternalRelay;
    private EmailAuthenticationMode selectedAuthenticationMode = EmailAuthenticationMode.Password;
    private string username = string.Empty;
    private string senderAddress = string.Empty;
    private string senderDisplayName = "AlarmSystem";
    private string recipientsText = string.Empty;
    private string destinationReference = string.Empty;
    private string timeoutSecondsText = "15";
    private string eventIdText;
    private string tag = "TEST.TAG";
    private string area = Strings.DefaultArea;
    private string equipment = Strings.DefaultEquipment;
    private AlarmSeverity selectedSeverity = AlarmSeverity.High;
    private string message = Strings.EmailDefaultMessage;
    private string sourceTimestampText;
    private string previewText = string.Empty;
    private string licenseStatus = Strings.EmailLicenseStatusUnknown;
    private string statusMessage = Strings.EmailStatusReady;
    private NotificationSendStatus? lastSendStatus;
    private string? lastErrorCode;
    private int? lastSmtpStatusCode;
    private bool hasPassword;
    private bool isRealSendConfirmed;
    private bool isBusy;
    private bool previewIsValid;
    private bool suppressPreviewUpdates;
    private CancellationTokenSource? operationCancellation;

    public EmailNotificationTestViewModel(
        EmailTemplateProvider templateProvider,
        IEmailNotificationTestService testService,
        IEmailSendConfirmationService confirmationService,
        TimeProvider timeProvider)
    {
        this.templateProvider = templateProvider;
        this.testService = testService;
        this.confirmationService = confirmationService;
        this.timeProvider = timeProvider;

        eventIdText = Guid.NewGuid().ToString("D");
        sourceTimestampText = FormatTimestamp(timeProvider.GetLocalNow());
        newTestDataCommand = new RelayCommand(NewTestData, () => !IsBusy);
        updatePreviewCommand = new RelayCommand(UpdatePreview, () => !IsBusy);
        cancelCommand = new RelayCommand(CancelOperation, () => IsBusy);
        UpdatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<EmailSelectionOption<EmailSecurityMode>> SecurityModes { get; } =
    [
        new(EmailSecurityMode.None, Strings.EmailSecurityModeNone),
        new(EmailSecurityMode.StartTls, Strings.EmailSecurityModeStartTls),
        new(EmailSecurityMode.SslTls, Strings.EmailSecurityModeSslTls),
    ];

    public IReadOnlyList<EmailSelectionOption<EmailAuthenticationMode>> AuthenticationModes
    { get; } =
    [
        new(EmailAuthenticationMode.None, Strings.EmailAuthenticationNone),
        new(EmailAuthenticationMode.Password, Strings.EmailAuthenticationPassword),
    ];

    public IReadOnlyList<EmailSelectionOption<AlarmSeverity>> SeverityOptions { get; } =
    [
        new(AlarmSeverity.High, Strings.SeverityHigh),
        new(AlarmSeverity.Medium, Strings.SeverityMedium),
        new(AlarmSeverity.Low, Strings.SeverityLow),
    ];

    public string SmtpHost
    {
        get => smtpHost;
        set => SetConnectionProperty(ref smtpHost, value);
    }

    public string SmtpPortText
    {
        get => smtpPortText;
        set => SetConnectionProperty(ref smtpPortText, value);
    }

    public EmailSecurityMode SelectedSecurityMode
    {
        get => selectedSecurityMode;
        set
        {
            if (SetConnectionProperty(ref selectedSecurityMode, value))
            {
                OnPropertyChanged(nameof(IsUnencryptedRelay));
            }
        }
    }

    public bool IsUnencryptedRelay => SelectedSecurityMode == EmailSecurityMode.None;

    public bool AllowUnencryptedInternalRelay
    {
        get => allowUnencryptedInternalRelay;
        set => SetConnectionProperty(ref allowUnencryptedInternalRelay, value);
    }

    public EmailAuthenticationMode SelectedAuthenticationMode
    {
        get => selectedAuthenticationMode;
        set
        {
            if (SetConnectionProperty(ref selectedAuthenticationMode, value))
            {
                OnPropertyChanged(nameof(UsesPasswordAuthentication));
            }
        }
    }

    public bool UsesPasswordAuthentication =>
        SelectedAuthenticationMode == EmailAuthenticationMode.Password;

    public string Username
    {
        get => username;
        set => SetConnectionProperty(ref username, value);
    }

    public string SenderAddress
    {
        get => senderAddress;
        set => SetConnectionProperty(ref senderAddress, value);
    }

    public string SenderDisplayName
    {
        get => senderDisplayName;
        set => SetConnectionProperty(ref senderDisplayName, value);
    }

    public string RecipientsText
    {
        get => recipientsText;
        set => SetConnectionProperty(ref recipientsText, value);
    }

    public string DestinationReference
    {
        get => destinationReference;
        set => SetConnectionProperty(ref destinationReference, value);
    }

    public string TimeoutSecondsText
    {
        get => timeoutSecondsText;
        set => SetConnectionProperty(ref timeoutSecondsText, value);
    }

    public string EventIdText
    {
        get => eventIdText;
        set => SetAlarmProperty(ref eventIdText, value);
    }

    public string Tag
    {
        get => tag;
        set => SetAlarmProperty(ref tag, value);
    }

    public string Area
    {
        get => area;
        set => SetAlarmProperty(ref area, value);
    }

    public string Equipment
    {
        get => equipment;
        set => SetAlarmProperty(ref equipment, value);
    }

    public AlarmSeverity SelectedSeverity
    {
        get => selectedSeverity;
        set => SetAlarmProperty(ref selectedSeverity, value);
    }

    public string Message
    {
        get => message;
        set => SetAlarmProperty(ref message, value);
    }

    public string SourceTimestampText
    {
        get => sourceTimestampText;
        set => SetAlarmProperty(ref sourceTimestampText, value);
    }

    public string PreviewText
    {
        get => previewText;
        private set => SetProperty(ref previewText, value);
    }

    public string LicenseStatus
    {
        get => licenseStatus;
        private set => SetProperty(ref licenseStatus, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public NotificationSendStatus? LastSendStatus
    {
        get => lastSendStatus;
        private set
        {
            if (SetProperty(ref lastSendStatus, value))
            {
                OnPropertyChanged(nameof(LastSendStatusDisplay));
            }
        }
    }

    public string? LastErrorCode
    {
        get => lastErrorCode;
        private set
        {
            if (SetProperty(ref lastErrorCode, value))
            {
                OnPropertyChanged(nameof(LastErrorCodeDisplay));
            }
        }
    }

    public int? LastSmtpStatusCode
    {
        get => lastSmtpStatusCode;
        private set
        {
            if (SetProperty(ref lastSmtpStatusCode, value))
            {
                OnPropertyChanged(nameof(LastSmtpStatusCodeDisplay));
            }
        }
    }

    public bool IsRealSendConfirmed
    {
        get => isRealSendConfirmed;
        set
        {
            if (SetProperty(ref isRealSendConfirmed, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                RaiseCanExecuteChanged();
                newTestDataCommand.RaiseCanExecuteChanged();
                updatePreviewCommand.RaiseCanExecuteChanged();
                cancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool CanTestConnection => !IsBusy && CanCreateOptions();

    public bool CanSend =>
        !IsBusy && IsRealSendConfirmed && previewIsValid && CanCreateOptions();

    public string LastSendStatusDisplay => LastSendStatus switch
    {
        NotificationSendStatus.Succeeded => Strings.StatusSucceeded,
        NotificationSendStatus.RetryableFailure => Strings.StatusRetryable,
        NotificationSendStatus.PermanentFailure => Strings.StatusPermanent,
        _ => Strings.NotAvailable,
    };

    public string LastErrorCodeDisplay => LastErrorCode ?? Strings.NotAvailable;

    public string LastSmtpStatusCodeDisplay =>
        LastSmtpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? Strings.NotAvailable;

    public ICommand NewTestDataCommand => newTestDataCommand;

    public ICommand UpdatePreviewCommand => updatePreviewCommand;

    public ICommand CancelCommand => cancelCommand;

    public void SetPasswordPresent(bool isPresent)
    {
        if (hasPassword == isPresent)
        {
            return;
        }

        hasPassword = isPresent;
        RaiseCanExecuteChanged();
    }

    public async Task RefreshLicenseStatusAsync()
    {
        try
        {
            var result = await testService.GetLicenseStatusAsync(CancellationToken.None);
            LicenseStatus = result.IsAuthorized
                ? Strings.EmailLicenseAuthorized
                : string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.EmailLicenseDeniedFormat,
                    result.FailureCode ?? LicenseAuthorizationFailureCodes.ValidationFailed);
        }
        catch (OperationCanceledException)
        {
            LicenseStatus = Strings.EmailLicenseStatusUnknown;
        }
    }

    public async Task TestConnectionAsync(string password)
    {
        if (IsBusy)
        {
            return;
        }

        if (!TryCreateOptions(password, out var options, out var validationMessage))
        {
            if (!string.IsNullOrEmpty(validationMessage))
            {
                StatusMessage = validationMessage;
            }

            return;
        }

        await ExecuteOperationAsync(
            token => testService.TestConnectionAsync(options, token),
            Strings.EmailStatusTestingConnection,
            Strings.EmailConnectionSucceeded);
        await RefreshLicenseStatusAsync();
    }

    public async Task SendAsync(string password)
    {
        if (IsBusy)
        {
            return;
        }

        if (!TryCreateOptions(password, out var options, out var validationMessage) ||
            !TryCreateNotification(out var notification, out validationMessage))
        {
            if (!string.IsNullOrEmpty(validationMessage))
            {
                StatusMessage = validationMessage;
            }

            return;
        }

        if (!IsRealSendConfirmed)
        {
            StatusMessage = Strings.EmailValidationConfirmationRequired;
            return;
        }

        bool confirmed;
        try
        {
            confirmed = confirmationService.Confirm(
                options.DestinationReference,
                options.SenderAddress,
                options.Recipients.Count,
                notification.EventId,
                notification.Tag);
        }
        catch (Exception)
        {
            StatusMessage = Strings.EmailStatusUnexpectedFailure;
            return;
        }

        if (!confirmed)
        {
            StatusMessage = Strings.EmailStatusConfirmationCancelled;
            return;
        }

        IsRealSendConfirmed = false;
        await ExecuteOperationAsync(
            token => testService.SendAsync(options, notification, token),
            Strings.EmailStatusSending,
            Strings.EmailSendSucceeded);
        await RefreshLicenseStatusAsync();
    }

    private async Task ExecuteOperationAsync(
        Func<CancellationToken, Task<NotificationSendResult>> operation,
        string pendingMessage,
        string successMessage)
    {
        ResetLastResult();
        StatusMessage = pendingMessage;
        IsBusy = true;
        operationCancellation = new CancellationTokenSource();
        try
        {
            var result = await operation(operationCancellation.Token);
            LastSendStatus = result.Status;
            LastErrorCode = result.ErrorCode;
            LastSmtpStatusCode = result.HttpStatusCode;
            StatusMessage = result.Status switch
            {
                NotificationSendStatus.Succeeded => successMessage,
                NotificationSendStatus.RetryableFailure => string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.EmailStatusRetryableFormat,
                    result.ErrorCode ?? Strings.NotAvailable),
                NotificationSendStatus.PermanentFailure => string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.EmailStatusPermanentFormat,
                    result.ErrorCode ?? Strings.NotAvailable),
                _ => Strings.EmailStatusUnexpectedFailure,
            };
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            StatusMessage = Strings.StatusCancelled;
        }
        catch (Exception)
        {
            StatusMessage = Strings.EmailStatusUnexpectedFailure;
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    private void NewTestData()
    {
        suppressPreviewUpdates = true;
        EventIdText = Guid.NewGuid().ToString("D");
        SourceTimestampText = FormatTimestamp(timeProvider.GetLocalNow());
        suppressPreviewUpdates = false;
        UpdatePreview();
    }

    private void CancelOperation() => operationCancellation?.Cancel();

    private void UpdatePreview()
    {
        previewIsValid = false;
        if (!TryCreateNotification(out var notification, out var validationMessage))
        {
            PreviewText = validationMessage;
            RaiseCanExecuteChanged();
            return;
        }

        try
        {
            if (!templateProvider.TryRender(notification, out var content))
            {
                PreviewText = Strings.EmailValidationTemplateUnhealthy;
                RaiseCanExecuteChanged();
                return;
            }

            PreviewText = string.Join(
                Environment.NewLine,
                Strings.EmailPreviewSubjectLabel,
                content!.Subject,
                string.Empty,
                Strings.EmailPreviewPlainTextLabel,
                content.PlainTextBody,
                string.Empty,
                Strings.EmailPreviewHtmlLabel,
                content.HtmlBody);
            previewIsValid = true;
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          InvalidOperationException)
        {
            PreviewText = Strings.EmailValidationPreview;
        }

        RaiseCanExecuteChanged();
    }

    private bool CanCreateOptions()
    {
        if (UsesPasswordAuthentication && !hasPassword)
        {
            return false;
        }

        return TryParsePort(out _) &&
            TryParseTimeout(out _) &&
            ParseRecipients().Count > 0 &&
            !string.IsNullOrWhiteSpace(SmtpHost) &&
            !string.IsNullOrWhiteSpace(SenderAddress) &&
            !string.IsNullOrWhiteSpace(DestinationReference);
    }

    private bool TryCreateOptions(
        string password,
        out EmailOptions options,
        out string validationMessage)
    {
        options = null!;
        validationMessage = string.Empty;
        if (!TryParsePort(out var port) || !TryParseTimeout(out var timeout))
        {
            validationMessage = Strings.EmailValidationPortOrTimeout;
            return false;
        }

        if (UsesPasswordAuthentication && string.IsNullOrWhiteSpace(password))
        {
            SetPasswordPresent(false);
            validationMessage = Strings.EmailValidationPasswordRequired;
            return false;
        }

        options = new EmailOptions
        {
            Enabled = true,
            Host = SmtpHost,
            Port = port,
            SecurityMode = SelectedSecurityMode,
            AllowUnencryptedInternalRelay = AllowUnencryptedInternalRelay,
            AuthenticationMode = SelectedAuthenticationMode,
            Username = Username,
            Password = password,
            SenderAddress = SenderAddress,
            SenderDisplayName = SenderDisplayName,
            Recipients = ParseRecipients(),
            DestinationReference = DestinationReference,
            Timeout = timeout,
        };
        try
        {
            options.Validate();
            return true;
        }
        catch (InvalidOperationException)
        {
            options = null!;
            validationMessage = Strings.EmailValidationSettings;
            return false;
        }
    }

    private bool TryCreateNotification(
        out AlarmNotification notification,
        out string validationMessage)
    {
        notification = null!;
        if (!Guid.TryParse(EventIdText, out var eventId) ||
            string.IsNullOrWhiteSpace(Tag) ||
            string.IsNullOrWhiteSpace(Message) ||
            !DateTimeOffset.TryParseExact(
                SourceTimestampText,
                SourceTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var sourceTimestamp))
        {
            validationMessage = Strings.EmailValidationAlarmData;
            return false;
        }

        notification = new AlarmNotification(
            eventId,
            Tag,
            Area,
            AlarmEventType.Alarm,
            SelectedSeverity,
            Message,
            sourceTimestamp,
            Equipment);
        validationMessage = string.Empty;
        return true;
    }

    private IReadOnlyList<string> ParseRecipients() => RecipientsText
        .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(recipient => recipient.Trim())
        .Where(recipient => recipient.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private bool TryParsePort(out int port) =>
        int.TryParse(SmtpPortText, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
        port is >= 1 and <= 65535;

    private bool TryParseTimeout(out TimeSpan timeout)
    {
        timeout = default;
        if (!int.TryParse(
                TimeoutSecondsText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds) ||
            seconds is < 1 or > 120)
        {
            return false;
        }

        timeout = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private bool SetConnectionProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        var changed = SetProperty(ref field, value, propertyName);
        if (changed)
        {
            RaiseCanExecuteChanged();
        }

        return changed;
    }

    private bool SetAlarmProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        if (!suppressPreviewUpdates)
        {
            UpdatePreview();
        }

        return true;
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void RaiseCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanTestConnection));
        OnPropertyChanged(nameof(CanSend));
    }

    private void ResetLastResult()
    {
        LastSendStatus = null;
        LastErrorCode = null;
        LastSmtpStatusCode = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToString(SourceTimestampFormat, CultureInfo.InvariantCulture);
}

public sealed record EmailSelectionOption<T>(T Value, string DisplayName)
    where T : struct, Enum;
