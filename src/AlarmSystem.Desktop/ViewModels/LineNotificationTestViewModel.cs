using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.Desktop.ViewModels;

public sealed class LineNotificationTestViewModel : INotifyPropertyChanged
{
    private const string SourceTimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private readonly LineAlarmTextRenderer renderer;
    private readonly ILineNotificationTestService testService;
    private readonly ILineSendConfirmationService confirmationService;
    private readonly TimeProvider timeProvider;
    private readonly RelayCommand newTestDataCommand;
    private readonly RelayCommand updatePreviewCommand;
    private readonly RelayCommand cancelCommand;

    private string groupId = string.Empty;
    private string timeoutSecondsText = "10";
    private string eventIdText;
    private string tag = "TEST.TAG";
    private string area = Strings.DefaultArea;
    private string equipment = Strings.DefaultEquipment;
    private AlarmEventType selectedEventType = AlarmEventType.Alarm;
    private AlarmSeverity selectedSeverity = AlarmSeverity.High;
    private string message = Strings.DefaultMessage;
    private string sourceTimestampText;
    private string previewText = string.Empty;
    private string statusMessage = Strings.StatusReady;
    private NotificationSendStatus? lastSendStatus;
    private string? lastErrorCode;
    private int? lastHttpStatusCode;
    private string? lastExternalMessageId;
    private TimeSpan? elapsedTime;
    private bool hasChannelAccessToken;
    private bool isRealSendConfirmed;
    private bool isSending;
    private bool previewIsValid;
    private bool suppressPreviewUpdates;
    private CancellationTokenSource? sendCancellation;

    public LineNotificationTestViewModel(
        LineAlarmTextRenderer renderer,
        ILineNotificationTestService testService,
        ILineSendConfirmationService confirmationService,
        TimeProvider timeProvider)
    {
        this.renderer = renderer;
        this.testService = testService;
        this.confirmationService = confirmationService;
        this.timeProvider = timeProvider;

        eventIdText = Guid.NewGuid().ToString("D");
        sourceTimestampText = FormatTimestamp(timeProvider.GetLocalNow());
        newTestDataCommand = new RelayCommand(NewTestData, () => !IsSending);
        updatePreviewCommand = new RelayCommand(UpdatePreview, () => !IsSending);
        cancelCommand = new RelayCommand(CancelSend, () => IsSending);
        UpdatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApiEndpoint => LineMessagingApiNotificationChannel.Endpoint.AbsoluteUri;

    public string GroupId
    {
        get => groupId;
        set
        {
            if (SetProperty(ref groupId, value))
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

    public AlarmEventType SelectedEventType
    {
        get => selectedEventType;
        set => SetAlarmProperty(ref selectedEventType, value);
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
                cancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotSending => !IsSending;

    public bool CanSend =>
        !IsSending &&
        hasChannelAccessToken &&
        IsRealSendConfirmed &&
        !string.IsNullOrWhiteSpace(GroupId) &&
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

    public ICommand CancelCommand => cancelCommand;

    public void SetChannelAccessTokenPresent(bool isPresent)
    {
        if (hasChannelAccessToken == isPresent)
        {
            return;
        }

        hasChannelAccessToken = isPresent;
        RaiseCanSendChanged();
    }

    public async Task SendAsync(string channelAccessToken)
    {
        if (IsSending)
        {
            return;
        }

        if (!TryPrepareSend(channelAccessToken, out var notification, out var timeout))
        {
            return;
        }

        var maskedGroupId = MaskGroupId(GroupId);
        bool confirmed;
        try
        {
            confirmed = confirmationService.Confirm(
                maskedGroupId,
                notification.EventId,
                notification.Severity,
                notification.Tag);
        }
        catch (Exception)
        {
            StatusMessage = Strings.StatusUnexpectedFailure;
            return;
        }

        if (!confirmed)
        {
            StatusMessage = Strings.StatusConfirmationCancelled;
            return;
        }

        IsRealSendConfirmed = false;
        ResetLastResult();
        StatusMessage = Strings.StatusSending;
        IsSending = true;
        sendCancellation = new CancellationTokenSource();
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            var result = await testService.SendAsync(
                GroupId,
                channelAccessToken,
                timeout,
                notification,
                sendCancellation.Token);
            ElapsedTime = timeProvider.GetElapsedTime(startedAt);
            ApplyResult(result, notification, maskedGroupId);
        }
        catch (OperationCanceledException) when (sendCancellation.IsCancellationRequested)
        {
            ElapsedTime = timeProvider.GetElapsedTime(startedAt);
            StatusMessage = Strings.StatusCancelled;
        }
        catch (Exception)
        {
            ElapsedTime = timeProvider.GetElapsedTime(startedAt);
            StatusMessage = Strings.StatusUnexpectedFailure;
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
            PreviewText = renderer.Render(notification);
            previewIsValid = true;
        }
        catch (InvalidOperationException)
        {
            PreviewText = Strings.ValidationPreviewTooLong;
        }
        catch (ArgumentException)
        {
            PreviewText = Strings.ValidationPreview;
        }

        RaiseCanSendChanged();
    }

    private bool TryPrepareSend(
        string channelAccessToken,
        out AlarmNotification notification,
        out TimeSpan timeout)
    {
        notification = null!;
        timeout = default;

        if (string.IsNullOrWhiteSpace(GroupId))
        {
            StatusMessage = Strings.ValidationGroupIdRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(channelAccessToken))
        {
            SetChannelAccessTokenPresent(false);
            StatusMessage = Strings.ValidationTokenRequired;
            return false;
        }

        if (!TryParseTimeout(out timeout))
        {
            StatusMessage = Strings.ValidationTimeout;
            return false;
        }

        if (!previewIsValid || !TryCreateNotification(out notification, out _))
        {
            StatusMessage = Strings.ValidationAlarmData;
            return false;
        }

        if (!IsRealSendConfirmed)
        {
            StatusMessage = Strings.ValidationConfirmationRequired;
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
            SelectedEventType,
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
        string maskedGroupId)
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
                Strings.StatusSucceededFormat,
                notification.EventId.ToString("D"),
                maskedGroupId,
                result.ExternalMessageId ?? Strings.NotAvailable,
                elapsed),
            NotificationSendStatus.RetryableFailure => string.Format(
                CultureInfo.CurrentCulture,
                Strings.StatusRetryableFormat,
                result.ErrorCode ?? Strings.NotAvailable,
                result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? Strings.NotAvailable,
                elapsed),
            NotificationSendStatus.PermanentFailure => string.Format(
                CultureInfo.CurrentCulture,
                Strings.StatusPermanentFormat,
                result.ErrorCode ?? Strings.NotAvailable,
                result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? Strings.NotAvailable,
                elapsed),
            _ => Strings.StatusUnexpectedFailure,
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

    private bool SetAlarmProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    private static string MaskGroupId(string groupId)
    {
        var value = groupId.Trim();
        if (value.Length == 0)
        {
            return Strings.NotAvailable;
        }

        if (value.Length <= 8)
        {
            return new string('*', value.Length);
        }

        return value[0] + new string('*', value.Length - 5) + value[^4..];
    }
}
