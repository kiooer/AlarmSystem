using System.Globalization;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.Desktop.Tests.ViewModels;

public sealed class LineNotificationTestViewModelTests
{
    [Fact]
    public void Constructor_InitializesExpectedTestDataAndPreview()
    {
        var timeProvider = CreateTimeProvider();
        var viewModel = CreateViewModel(timeProvider: timeProvider);

        Assert.True(Guid.TryParse(viewModel.EventIdText, out _));
        Assert.Equal("TEST.TAG", viewModel.Tag);
        Assert.Equal(Strings.DefaultArea, viewModel.Area);
        Assert.Equal(Strings.DefaultEquipment, viewModel.Equipment);
        Assert.Equal(AlarmEventType.Alarm, viewModel.SelectedEventType);
        Assert.Equal(AlarmSeverity.High, viewModel.SelectedSeverity);
        Assert.Equal(Strings.DefaultMessage, viewModel.Message);
        Assert.Equal("2026-10-11 16:30:00 +08:00", viewModel.SourceTimestampText);
        Assert.Contains("等級：HIGH", viewModel.PreviewText, StringComparison.Ordinal);
        Assert.Contains("Tag：TEST.TAG", viewModel.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void NewTestData_ChangesEventIdAndTimestampWithoutChangingDestination()
    {
        var timeProvider = CreateTimeProvider();
        var viewModel = CreateViewModel(timeProvider: timeProvider);
        viewModel.GroupId = "desktop-group-do-not-persist";
        var originalEventId = viewModel.EventIdText;
        var originalTimestamp = viewModel.SourceTimestampText;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);

        viewModel.NewTestDataCommand.Execute(null);

        Assert.NotEqual(originalEventId, viewModel.EventIdText);
        Assert.NotEqual(originalTimestamp, viewModel.SourceTimestampText);
        Assert.Equal("desktop-group-do-not-persist", viewModel.GroupId);
    }

    [Fact]
    public void CanSend_IsFalseWithoutGroupId()
    {
        var viewModel = CreateViewModel();
        viewModel.SetChannelAccessTokenPresent(true);
        viewModel.IsRealSendConfirmed = true;

        Assert.False(viewModel.CanSend);
    }

    [Fact]
    public void CanSend_IsFalseWithoutCredential()
    {
        var viewModel = CreateViewModel();
        viewModel.GroupId = "desktop-test-group";
        viewModel.IsRealSendConfirmed = true;

        Assert.False(viewModel.CanSend);
    }

    [Fact]
    public void CanSend_IsFalseWithoutExplicitConfirmation()
    {
        var viewModel = CreateViewModel();
        viewModel.GroupId = "desktop-test-group";
        viewModel.SetChannelAccessTokenPresent(true);

        Assert.False(viewModel.CanSend);
    }

    [Fact]
    public void CanSend_IsTrueWhenInputsPreviewAndConfirmationAreValid()
    {
        var viewModel = CreateReadyViewModel();

        Assert.True(viewModel.CanSend);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("1.5")]
    public void CanSend_IsFalseWhenTimeoutIsInvalid(string timeoutSeconds)
    {
        var viewModel = CreateReadyViewModel();

        viewModel.TimeoutSecondsText = timeoutSeconds;

        Assert.False(viewModel.CanSend);
    }

    [Fact]
    public void Preview_RejectsMessageOverOfficialLineLimit()
    {
        var viewModel = CreateReadyViewModel();

        viewModel.Message = new string('x', LineAlarmTextRenderer.MaxTextLength + 1);

        Assert.Equal(Strings.ValidationPreviewTooLong, viewModel.PreviewText);
        Assert.False(viewModel.CanSend);
    }

    [Fact]
    public void Preview_IsProducedByOfficialRenderer()
    {
        var renderer = new LineAlarmTextRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        var notification = new AlarmNotification(
            Guid.Parse(viewModel.EventIdText),
            viewModel.Tag,
            viewModel.Area,
            viewModel.SelectedEventType,
            viewModel.SelectedSeverity,
            viewModel.Message,
            DateTimeOffset.ParseExact(
                viewModel.SourceTimestampText,
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture),
            viewModel.Equipment);

        Assert.Equal(renderer.Render(notification), viewModel.PreviewText);
    }

    [Fact]
    public async Task SendAsync_DoesNotSendTwiceWhileRequestIsInProgress()
    {
        var completion = new TaskCompletionSource<NotificationSendResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubLineNotificationTestService
        {
            Handler = (_, _, _, _, _) => completion.Task,
        };
        var viewModel = CreateReadyViewModel(service: service);

        var firstSend = viewModel.SendAsync("desktop-test-credential");
        await WaitUntilAsync(() => viewModel.IsSending);
        await viewModel.SendAsync("desktop-test-credential");

        Assert.Equal(1, service.CallCount);
        Assert.False(viewModel.CanSend);
        completion.SetResult(NotificationSendResult.Success("message-1"));
        await firstSend;
    }

    [Fact]
    public async Task SendAsync_DisplaysSuccessfulResultWithoutSecrets()
    {
        const string groupId = "desktop-group-destination-1234";
        const string credential = "desktop-test-credential";
        var service = new StubLineNotificationTestService
        {
            Handler = (_, _, _, _, _) =>
                Task.FromResult(NotificationSendResult.Success("message-123")),
        };
        var confirmation = new StubConfirmationService();
        var viewModel = CreateReadyViewModel(service, confirmation, groupId: groupId);

        await viewModel.SendAsync(credential);

        Assert.Equal(NotificationSendStatus.Succeeded, viewModel.LastSendStatus);
        Assert.Equal("message-123", viewModel.LastExternalMessageId);
        Assert.Contains(Strings.StatusSucceeded, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(groupId, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.NotEqual(groupId, confirmation.MaskedGroupId);
        Assert.EndsWith("1234", confirmation.MaskedGroupId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NotificationSendStatus.RetryableFailure, "line_timeout", 503)]
    [InlineData(NotificationSendStatus.PermanentFailure, "line_http_error", 401)]
    public async Task SendAsync_DisplaysFailureClassification(
        NotificationSendStatus status,
        string errorCode,
        int httpStatusCode)
    {
        var result = new NotificationSendResult(status, errorCode, httpStatusCode);
        var service = new StubLineNotificationTestService
        {
            Handler = (_, _, _, _, _) => Task.FromResult(result),
        };
        var viewModel = CreateReadyViewModel(service);

        await viewModel.SendAsync("desktop-test-credential");

        Assert.Equal(status, viewModel.LastSendStatus);
        Assert.Equal(errorCode, viewModel.LastErrorCode);
        Assert.Equal(httpStatusCode, viewModel.LastHttpStatusCode);
        Assert.Contains(errorCode, viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelCommand_CancelsInProgressSendWithoutFailureStatus()
    {
        var service = new StubLineNotificationTestService
        {
            Handler = async (_, _, _, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return NotificationSendResult.Success();
            },
        };
        var viewModel = CreateReadyViewModel(service);

        var send = viewModel.SendAsync("desktop-test-credential");
        await WaitUntilAsync(() => viewModel.IsSending);
        viewModel.CancelCommand.Execute(null);
        await send;

        Assert.Equal(Strings.StatusCancelled, viewModel.StatusMessage);
        Assert.Null(viewModel.LastSendStatus);
        Assert.False(viewModel.IsSending);
    }

    [Fact]
    public async Task SendAsync_UnexpectedExceptionUsesGenericNonSensitiveMessage()
    {
        const string groupId = "desktop-private-group-1234";
        const string credential = "desktop-private-credential";
        var service = new StubLineNotificationTestService
        {
            Handler = (_, _, _, _, _) => throw new InvalidOperationException(
                $"Simulated failure containing {credential} and {groupId}."),
        };
        var viewModel = CreateReadyViewModel(service, groupId: groupId);

        await viewModel.SendAsync(credential);

        Assert.Equal(Strings.StatusUnexpectedFailure, viewModel.StatusMessage);
        Assert.DoesNotContain(groupId, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_DoesNotCallServiceWhenConfirmationDialogIsCancelled()
    {
        var service = new StubLineNotificationTestService();
        var confirmation = new StubConfirmationService { Result = false };
        var viewModel = CreateReadyViewModel(service, confirmation);

        await viewModel.SendAsync("desktop-test-credential");

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.StatusConfirmationCancelled, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SendAsync_FullyMasksShortGroupIdInConfirmation()
    {
        var service = new StubLineNotificationTestService();
        var confirmation = new StubConfirmationService { Result = false };
        var viewModel = CreateReadyViewModel(service, confirmation, groupId: "C123");

        await viewModel.SendAsync("desktop-test-credential");

        Assert.Equal("****", confirmation.MaskedGroupId);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task SendAsync_DoesNotCallServiceWhenCredentialIsBlank()
    {
        var service = new StubLineNotificationTestService();
        var viewModel = CreateReadyViewModel(service);

        await viewModel.SendAsync("   ");

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.ValidationTokenRequired, viewModel.StatusMessage);
    }

    [Fact]
    public void ViewModel_DoesNotExposeBindableCredentialStringProperty()
    {
        var credentialProperties = typeof(LineNotificationTestViewModel)
            .GetProperties()
            .Where(property =>
                property.PropertyType == typeof(string) &&
                property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(credentialProperties);
    }

    private static LineNotificationTestViewModel CreateReadyViewModel(
        StubLineNotificationTestService? service = null,
        StubConfirmationService? confirmation = null,
        string groupId = "desktop-test-group-destination")
    {
        var viewModel = CreateViewModel(service, confirmation);
        viewModel.GroupId = groupId;
        viewModel.SetChannelAccessTokenPresent(true);
        viewModel.IsRealSendConfirmed = true;
        return viewModel;
    }

    private static LineNotificationTestViewModel CreateViewModel(
        StubLineNotificationTestService? service = null,
        StubConfirmationService? confirmation = null,
        MutableTimeProvider? timeProvider = null,
        LineAlarmTextRenderer? renderer = null) => new(
            renderer ?? new LineAlarmTextRenderer(),
            service ?? new StubLineNotificationTestService(),
            confirmation ?? new StubConfirmationService(),
            timeProvider ?? CreateTimeProvider());

    private static MutableTimeProvider CreateTimeProvider() => new(
        new DateTimeOffset(2026, 10, 11, 8, 30, 0, TimeSpan.Zero),
        TimeZoneInfo.CreateCustomTimeZone(
            "Test Taipei",
            TimeSpan.FromHours(8),
            "Test Taipei",
            "Test Taipei"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Yield();
        }

        Assert.True(condition(), "The expected asynchronous state was not reached.");
    }

    private sealed class StubLineNotificationTestService : ILineNotificationTestService
    {
        public Func<
            string,
            string,
            TimeSpan,
            AlarmNotification,
            CancellationToken,
            Task<NotificationSendResult>>
            Handler
        { get; init; } =
                (_, _, _, _, _) => Task.FromResult(NotificationSendResult.Success());

        public int CallCount { get; private set; }

        public Task<NotificationSendResult> SendAsync(
            string groupId,
            string channelAccessToken,
            TimeSpan timeout,
            AlarmNotification notification,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Handler(groupId, channelAccessToken, timeout, notification, cancellationToken);
        }
    }

    private sealed class StubConfirmationService : ILineSendConfirmationService
    {
        public bool Result { get; init; } = true;

        public string MaskedGroupId { get; private set; } = string.Empty;

        public bool Confirm(
            string maskedGroupId,
            Guid eventId,
            AlarmSeverity severity,
            string tag)
        {
            MaskedGroupId = maskedGroupId;
            return Result;
        }
    }

    private sealed class MutableTimeProvider(
        DateTimeOffset utcNow,
        TimeZoneInfo localTimeZone) : TimeProvider
    {
        private long timestamp;

        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override TimeZoneInfo LocalTimeZone => localTimeZone;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override long GetTimestamp() =>
            Interlocked.Add(ref timestamp, TimeSpan.TicksPerMillisecond);
    }
}
