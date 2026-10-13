using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.Desktop.ViewModels;

public sealed class TeamsNotificationTestViewModel : INotifyPropertyChanged
{
    private const string SourceTimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private readonly TeamsCardTemplateProvider templateProvider;
    private readonly ITeamsNotificationTestService testService;
    private readonly ITeamsSendConfirmationService confirmationService;
    private readonly TimeProvider timeProvider;
    private readonly RelayCommand newTestDataCommand;
    private readonly RelayCommand updatePreviewCommand;
    private readonly RelayCommand reloadTemplateCommand;
    private readonly RelayCommand cancelCommand;

    private string destinationReference = Strings.TeamsDefaultDestinationReference;
    private string timeoutSecondsText = "10";
    private string eventIdText;
    private string tag = "TEST.TAG";
    private string area = Strings.DefaultArea;
    private string equipment = Strings.DefaultEquipment;
    private AlarmSeverity selectedSeverity = AlarmSeverity.High;
    private string message = Strings.TeamsDefaultMessage;
    private string sourceTimestampText;
    private string previewText = string.Empty;
    private string statusMessage = Strings.TeamsStatusReady;
    private NotificationSendStatus? lastSendStatus;
    private string? lastErrorCode;
    private int? lastHttpStatusCode;
    private string? lastExternalMessageId;
    private TimeSpan? elapsedTime;
    private bool hasWebhookUrl;
    private bool isRealSendConfirmed;
    private bool isSending;
    private bool previewIsValid;
    private bool suppressPreviewUpdates;
    private CancellationTokenSource? sendCancellation;

    public TeamsNotificationTestViewModel(
        TeamsCardTemplateProvider templateProvider,
        ITeamsNotificationTestService testService,
        ITeamsSendConfirmationService confirmationService,
        TimeProvider timeProvider)
    {
        this.templateProvider = templateProvider;
        this.testService = testService;
        this.confirmationService = confirmationService;
        this.timeProvider = timeProvider;

        eventIdText = Guid.NewGuid().ToString("D");
        sourceTimestampText = FormatTimestamp(timeProvider.GetLocalNow());
        newTestDataCommand = new RelayCommand(NewTestData, () => !IsSending);
        updatePreviewCommand = new RelayCommand(UpdatePreview, () => !IsSending);
        reloadTemplateCommand = new RelayCommand(ReloadTemplate, () => !IsSending);
        cancelCommand = new RelayCommand(CancelSend, () => IsSending);
        UpdatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ManualTestLicenseNotice => Strings.ManualTestLicenseNotice;

    public string DestinationReference
    {
        get => destinationReference;
        set
        {
            if (SetProperty(ref destinationReference, value))
            {
                RaiseCanSendChanged();
            }
        }
    }

    public string TimeoutSecondsText
    {
        get => timeoutSecondsText;
        set
        {
            if (SetProperty(ref timeoutSecondsText, value))
            {
                RaiseCanSendChanged();
            }
        }
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

    public int? LastHttpStatusCode
    {
        get => lastHttpStatusCode;
        private set
        {
            if (SetProperty(ref lastHttpStatusCode, value))
            {
                OnPropertyChanged(nameof(LastHttpStatusCodeDisplay));
            }
        }
    }

    public string? LastExternalMessageId
    {
        get => lastExternalMessageId;
        private set
        {
            if (SetProperty(ref lastExternalMessageId, value))
            {
                OnPropertyChanged(nameof(LastExternalMessageIdDisplay));
            }
        }
    }

    public TimeSpan? ElapsedTime
    {
        get => elapsedTime;
        private set
        {
            if (SetProperty(ref elapsedTime, value))
            {
                OnPropertyChanged(nameof(ElapsedTimeDisplay));
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
                RaiseCanSendChanged();
            }
        }
    }

    public bool IsSending
    {
        get => isSending;
        private set
        {
            if (SetProperty(ref isSending, value))
            {
                OnPropertyChanged(nameof(IsNotSending));
                RaiseCanSendChanged();
                newTestDataCommand.RaiseCanExecuteChanged();
                updatePreviewCommand.RaiseCanExecuteChanged();
                reloadTemplateCommand.RaiseCanExecuteChanged();
                cancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotSending => !IsSending;

    public bool CanSend =>
        !IsSending &&
        hasWebhookUrl &&
        IsRealSendConfirmed &&
        !string.IsNullOrWhiteSpace(DestinationReference) &&
        TryParseTimeout(out _) &&
        previewIsValid;

    public string LastSendStatusDisplay => LastSendStatus switch
    {
        NotificationSendStatus.Succeeded => Strings.StatusSucceeded,
        NotificationSendStatus.RetryableFailure => Strings.StatusRetryable,
        NotificationSendStatus.PermanentFailure => Strings.StatusPermanent,
        _ => Strings.NotAvailable,
    };

    public string LastErrorCodeDisplay => LastErrorCode ?? Strings.NotAvailable;

    public string LastHttpStatusCodeDisplay =>
        LastHttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? Strings.NotAvailable;

    public string LastExternalMessageIdDisplay =>
        LastExternalMessageId ?? Strings.NotAvailable;

    public string ElapsedTimeDisplay => ElapsedTime is null
        ? Strings.NotAvailable
        : FormatElapsed(ElapsedTime.Value);

    public ICommand NewTestDataCommand => newTestDataCommand;

    public ICommand UpdatePreviewCommand => updatePreviewCommand;

    public ICommand ReloadTemplateCommand => reloadTemplateCommand;

    public ICommand CancelCommand => cancelCommand;

    public void SetWebhookUrlPresent(bool isPresent)
    {
        if (hasWebhookUrl == isPresent)
        {
            return;
        }

        hasWebhookUrl = isPresent;
        RaiseCanSendChanged();
    }

    public async Task SendAsync(string webhookUrl)
    {
        if (IsSending)
        {
            return;
        }

        if (!TryPrepareSend(
                webhookUrl,
                out var endpoint,
                out var destinationSnapshot,
                out var timeout,
                out var notification))
        {
            return;
        }

        var maskedEndpoint = MaskEndpoint(endpoint);
        bool confirmed;
        try
        {
            confirmed = confirmationService.Confirm(
                maskedEndpoint,
                destinationSnapshot,
                notification.EventId,
                notification.Severity,
                notification.Tag);
        }
        catch (Exception)
        {
            StatusMessage = Strings.TeamsStatusUnexpectedFailure;
            return;
        }

        IsRealSendConfirmed = false;
        if (!confirmed)
        {
            StatusMessage = Strings.TeamsStatusConfirmationCancelled;
            return;
        }

        ResetLastResult();
        StatusMessage = Strings.TeamsStatusSending;
        IsSending = true;
        sendCancellation = new CancellationTokenSource();
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            var result = await testService.SendAsync(
                webhookUrl,
                destinationSnapshot,
                timeout,
                notification,
                sendCancellation.Token);
            ElapsedTime = timeProvider.GetElapsedTime(startedAt);
            ApplyResult(result, notification, maskedEndpoint);
        }
        catch (OperationCanceledException) when (sendCancellation.IsCancellationRequested)
        {
            ElapsedTime = timeProvider.GetElapsedTime(startedAt);
            StatusMessage = Strings.TeamsStatusCancelledUncertain;
        }
        catch (Exception)
        {
            ElapsedTime = timeProvider.GetElapsedTime(startedAt);
            StatusMessage = Strings.TeamsStatusUnexpectedFailure;
        }
        finally
        {
            sendCancellation.Dispose();
            sendCancellation = null;
            IsSending = false;
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

    private void ReloadTemplate()
    {
        templateProvider.Reload();
        UpdatePreview();
        if (previewIsValid)
        {
            StatusMessage = Strings.TeamsStatusTemplateReloaded;
        }
    }

    private void CancelSend() => sendCancellation?.Cancel();

    private void UpdatePreview()
    {
        previewIsValid = false;
        if (!TryCreateNotification(out var notification, out var validationMessage))
        {
            PreviewText = validationMessage;
            RaiseCanSendChanged();
            return;
        }

        try
        {
            if (!templateProvider.TryRender(notification, out var card))
            {
                PreviewText = Strings.TeamsValidationTemplateUnhealthy;
                RaiseCanSendChanged();
                return;
            }

            PreviewText = card!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            previewIsValid = true;
        }
        catch (InvalidOperationException)
        {
            PreviewText = Strings.TeamsValidationPreview;
        }
        catch (ArgumentException)
        {
            PreviewText = Strings.TeamsValidationPreview;
        }

        RaiseCanSendChanged();
    }

    private bool TryPrepareSend(
        string webhookUrl,
        out Uri endpoint,
        out string destinationSnapshot,
        out TimeSpan timeout,
        out AlarmNotification notification)
    {
        endpoint = null!;
        destinationSnapshot = DestinationReference.Trim();
        timeout = default;
        notification = null!;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            SetWebhookUrlPresent(false);
            StatusMessage = Strings.TeamsValidationEndpointRequired;
            return false;
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var parsedEndpoint) ||
            parsedEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            StatusMessage = Strings.TeamsValidationEndpoint;
            return false;
        }

        endpoint = parsedEndpoint;

        if (string.IsNullOrWhiteSpace(destinationSnapshot))
        {
            StatusMessage = Strings.TeamsValidationDestinationReference;
            return false;
        }

        if (!TryParseTimeout(out timeout))
        {
            StatusMessage = Strings.ValidationTimeout;
            return false;
        }

        try
        {
            new TeamsOptions
            {
                Enabled = true,
                Endpoint = endpoint,
                DestinationReference = destinationSnapshot,
                Timeout = timeout,
            }.Validate();
        }
        catch (InvalidOperationException)
        {
            StatusMessage = Strings.TeamsValidationSettings;
            return false;
        }

        if (!previewIsValid || !TryCreateNotification(out notification, out _))
        {
            StatusMessage = Strings.TeamsValidationAlarmData;
            return false;
        }

        if (!IsRealSendConfirmed)
        {
            StatusMessage = Strings.TeamsValidationConfirmationRequired;
            return false;
        }

        return true;
    }

    private bool TryCreateNotification(
        out AlarmNotification notification,
        out string validationMessage)
    {
        notification = null!;

        if (!Guid.TryParse(EventIdText, out var eventId))
        {
            validationMessage = Strings.ValidationEventId;
            return false;
        }

        if (string.IsNullOrWhiteSpace(Tag))
        {
            validationMessage = Strings.ValidationTag;
            return false;
        }

        if (string.IsNullOrWhiteSpace(Message))
        {
            validationMessage = Strings.ValidationMessage;
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                SourceTimestampText,
                SourceTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var sourceTimestamp))
        {
            validationMessage = Strings.ValidationTimestamp;
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

    private bool TryParseTimeout(out TimeSpan timeout)
    {
        timeout = default;
        if (!int.TryParse(
                TimeoutSecondsText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timeoutSeconds) ||
            timeoutSeconds is < 1 or > 120)
        {
            return false;
        }

        timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return true;
    }

    private void ApplyResult(
        NotificationSendResult result,
        AlarmNotification notification,
        string maskedEndpoint)
    {
        LastSendStatus = result.Status;
        LastErrorCode = result.ErrorCode;
        LastHttpStatusCode = result.HttpStatusCode;
        LastExternalMessageId = result.ExternalMessageId;

        var elapsed = FormatElapsed(ElapsedTime ?? TimeSpan.Zero);
        StatusMessage = result.Status switch
        {
            NotificationSendStatus.Succeeded => string.Format(
                CultureInfo.CurrentCulture,
                Strings.TeamsStatusSucceededFormat,
                notification.EventId.ToString("D"),
                maskedEndpoint,
                result.ExternalMessageId ?? Strings.NotAvailable,
                elapsed),
            NotificationSendStatus.RetryableFailure => string.Format(
                CultureInfo.CurrentCulture,
                Strings.TeamsStatusRetryableFormat,
                result.ErrorCode ?? Strings.NotAvailable,
                result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? Strings.NotAvailable,
                elapsed),
            NotificationSendStatus.PermanentFailure => string.Format(
                CultureInfo.CurrentCulture,
                Strings.TeamsStatusPermanentFormat,
                result.ErrorCode ?? Strings.NotAvailable,
                result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? Strings.NotAvailable,
                elapsed),
            _ => Strings.TeamsStatusUnexpectedFailure,
        };
    }

    private void ResetLastResult()
    {
        LastSendStatus = null;
        LastErrorCode = null;
        LastHttpStatusCode = null;
        LastExternalMessageId = null;
        ElapsedTime = null;
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

    private void RaiseCanSendChanged() => OnPropertyChanged(nameof(CanSend));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToString(SourceTimestampFormat, CultureInfo.InvariantCulture);

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static string MaskEndpoint(Uri endpoint)
    {
        var port = endpoint.IsDefaultPort
            ? string.Empty
            : $":{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
        return $"{endpoint.Scheme}://{endpoint.Host}{port}/***";
    }
}
