using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;
using AlarmSystem.Domain.Events;
using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.Desktop.Tests.ViewModels;

public sealed class TeamsNotificationTestViewModelTests
{
    private const string TestEndpoint = "https://example.invalid/teams-test-hook/private-value";

    [Fact]
    public void Constructor_InitializesAlarmOnlyTestDataAndOfficialPreview()
    {
        var timeProvider = CreateTimeProvider();
        var templateProvider = CreateTemplateProvider();
        var viewModel = CreateViewModel(
            timeProvider: timeProvider,
            templateProvider: templateProvider);

        Assert.True(Guid.TryParse(viewModel.EventIdText, out var eventId));
        Assert.Equal("TEST.TAG", viewModel.Tag);
        Assert.Equal(Strings.DefaultArea, viewModel.Area);
        Assert.Equal(Strings.DefaultEquipment, viewModel.Equipment);
        Assert.Equal(AlarmSeverity.High, viewModel.SelectedSeverity);
        Assert.Equal(Strings.TeamsDefaultMessage, viewModel.Message);
        Assert.Equal(Strings.TeamsDefaultDestinationReference, viewModel.DestinationReference);
        Assert.Equal("10", viewModel.TimeoutSecondsText);
        Assert.Equal("2026-10-13 16:30:00 +08:00", viewModel.SourceTimestampText);

        var notification = new AlarmNotification(
            eventId,
            viewModel.Tag,
            viewModel.Area,
            AlarmEventType.Alarm,
            viewModel.SelectedSeverity,
            viewModel.Message,
            DateTimeOffset.ParseExact(
                viewModel.SourceTimestampText,
                "yyyy-MM-dd HH:mm:ss zzz",
                System.Globalization.CultureInfo.InvariantCulture),
            viewModel.Equipment);
        Assert.True(templateProvider.TryRender(notification, out var expectedCard));
        Assert.Equal(
            expectedCard!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            viewModel.PreviewText);
        Assert.Contains("Active / UnACK", viewModel.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualTestLicenseNotice_ExplainsDesktopOnlyLicenseException()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(Strings.ManualTestLicenseNotice, viewModel.ManualTestLicenseNotice);
        Assert.Contains("人工測試", Strings.TeamsToolNotice, StringComparison.Ordinal);
        Assert.Contains("不代表正式自動發報已取得授權", Strings.TeamsToolNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_DoesNotExposeBindableWebhookUrlString()
    {
        Assert.DoesNotContain(
            typeof(TeamsNotificationTestViewModel).GetProperties(),
            property =>
                property.PropertyType == typeof(string) &&
                property.Name.Contains("Webhook", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(AlarmSeverity.High, "HIGH", "Attention")]
    [InlineData(AlarmSeverity.Medium, "MEDIUM", "Warning")]
    [InlineData(AlarmSeverity.Low, "LOW", "Good")]
    public void Severity_UsesOfficialRendererMapping(
        AlarmSeverity severity,
        string label,
        string color)
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSeverity = severity;

        var card = JsonNode.Parse(viewModel.PreviewText)!.AsObject();
        Assert.Equal("AdaptiveCard", card["type"]!.GetValue<string>());
        Assert.Contains(label, viewModel.PreviewText, StringComparison.Ordinal);
        Assert.Contains(color, viewModel.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void NewTestData_ChangesEventIdAndTimestampWithoutChangingEndpointState()
    {
        var timeProvider = CreateTimeProvider();
        var viewModel = CreateReadyViewModel(timeProvider: timeProvider);
        var firstEventId = viewModel.EventIdText;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);

        viewModel.NewTestDataCommand.Execute(null);

        Assert.NotEqual(firstEventId, viewModel.EventIdText);
        Assert.Equal("2026-10-13 16:35:00 +08:00", viewModel.SourceTimestampText);
        Assert.True(viewModel.CanSend);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.invalid/test-hook")]
    [InlineData("relative/test-hook")]
    public async Task SendAsync_InvalidHttpsEndpointDoesNotCallService(string endpoint)
    {
        var service = new StubTeamsNotificationTestService();
        var viewModel = CreateReadyViewModel(service: service);

        await viewModel.SendAsync(endpoint);

        Assert.Equal(0, service.CallCount);
        Assert.Equal(
            string.IsNullOrWhiteSpace(endpoint)
                ? Strings.TeamsValidationEndpointRequired
                : Strings.TeamsValidationEndpoint,
            viewModel.StatusMessage);
    }

    [Fact]
    public async Task SendAsync_EmptyDestinationDoesNotCallService()
    {
        var service = new StubTeamsNotificationTestService();
        var viewModel = CreateReadyViewModel(service: service);
        viewModel.DestinationReference = " ";

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.TeamsValidationDestinationReference, viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("1.5")]
    public async Task SendAsync_InvalidTimeoutDoesNotCallService(string timeout)
    {
        var service = new StubTeamsNotificationTestService();
        var viewModel = CreateReadyViewModel(service: service);
        viewModel.TimeoutSecondsText = timeout;

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.ValidationTimeout, viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("EventId")]
    [InlineData("Tag")]
    [InlineData("Message")]
    [InlineData("Timestamp")]
    public async Task SendAsync_InvalidAlarmDataDoesNotCallService(string field)
    {
        var service = new StubTeamsNotificationTestService();
        var viewModel = CreateReadyViewModel(service: service);
        switch (field)
        {
            case "EventId":
                viewModel.EventIdText = "not-a-guid";
                break;
            case "Tag":
                viewModel.Tag = " ";
                break;
            case "Message":
                viewModel.Message = " ";
                break;
            case "Timestamp":
                viewModel.SourceTimestampText = "2026-10-13 16:30:00";
                break;
        }

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.TeamsValidationAlarmData, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SendAsync_FirstConfirmationIsRequired()
    {
        var service = new StubTeamsNotificationTestService();
        var viewModel = CreateReadyViewModel(service: service);
        viewModel.IsRealSendConfirmed = false;

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.TeamsValidationConfirmationRequired, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SendAsync_SecondConfirmationRejectsWithoutHttpAndUsesMaskedEndpoint()
    {
        var service = new StubTeamsNotificationTestService();
        var confirmation = new StubConfirmationService { Result = false };
        var viewModel = CreateReadyViewModel(service, confirmation);

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.TeamsStatusConfirmationCancelled, viewModel.StatusMessage);
        Assert.False(viewModel.IsRealSendConfirmed);
        Assert.Equal("https://example.invalid/***", confirmation.MaskedEndpoint);
        Assert.DoesNotContain("private-value", confirmation.MaskedEndpoint, StringComparison.Ordinal);
        Assert.Equal(Strings.TeamsDefaultDestinationReference, confirmation.DestinationReference);
    }

    [Fact]
    public async Task SendAsync_CapturesSettingsAndAlarmSnapshot()
    {
        var service = new StubTeamsNotificationTestService();
        var viewModel = CreateReadyViewModel(service: service);
        viewModel.DestinationReference = "OperationsTest";
        viewModel.TimeoutSecondsText = "12";
        viewModel.SelectedSeverity = AlarmSeverity.Medium;

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(1, service.CallCount);
        Assert.Equal(TestEndpoint, service.WebhookUrl);
        Assert.Equal("OperationsTest", service.DestinationReference);
        Assert.Equal(TimeSpan.FromSeconds(12), service.Timeout);
        Assert.Equal(AlarmEventType.Alarm, service.Notification!.EventType);
        Assert.Equal(AlarmSeverity.Medium, service.Notification.Severity);
        Assert.False(viewModel.IsRealSendConfirmed);
    }

    [Fact]
    public async Task SendAsync_RepeatedClickAllowsOnlyOneOperation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<NotificationSendResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubTeamsNotificationTestService
        {
            Handler = (_, _, _, _, _) =>
            {
                started.TrySetResult();
                return release.Task;
            },
        };
        var viewModel = CreateReadyViewModel(service: service);

        var firstSend = viewModel.SendAsync(TestEndpoint);
        await started.Task;
        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(1, service.CallCount);
        Assert.True(viewModel.IsSending);
        release.SetResult(NotificationSendResult.Success());
        await firstSend;
        Assert.False(viewModel.IsSending);
    }

    [Fact]
    public async Task CancelCommand_CancelsOperationAndWarnsResultMayBeUncertain()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubTeamsNotificationTestService
        {
            Handler = async (_, _, _, _, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return NotificationSendResult.Success();
            },
        };
        var viewModel = CreateReadyViewModel(service: service);

        var send = viewModel.SendAsync(TestEndpoint);
        await started.Task;
        viewModel.CancelCommand.Execute(null);
        await send;

        Assert.False(viewModel.IsSending);
        Assert.Equal(Strings.TeamsStatusCancelledUncertain, viewModel.StatusMessage);
        Assert.Null(viewModel.LastSendStatus);
    }

    [Fact]
    public async Task UnhealthyTemplatePreventsSend()
    {
        var service = new StubTeamsNotificationTestService();
        var provider = new TeamsCardTemplateProvider(Path.Combine(
            AppContext.BaseDirectory,
            "templates",
            "missing-teams-template.json"));
        var viewModel = CreateReadyViewModel(service: service, templateProvider: provider);

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(Strings.TeamsValidationTemplateUnhealthy, viewModel.PreviewText);
        Assert.Equal(0, service.CallCount);
        Assert.Equal(Strings.TeamsValidationAlarmData, viewModel.StatusMessage);
    }

    [Fact]
    public void ReloadTemplateCommand_RecoversPreviewAfterTemplateIsCorrected()
    {
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"alarm-system-desktop-teams-template-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(temporaryPath, "{invalid-json}");
            var provider = new TeamsCardTemplateProvider(temporaryPath);
            var viewModel = CreateViewModel(templateProvider: provider);
            Assert.Equal(Strings.TeamsValidationTemplateUnhealthy, viewModel.PreviewText);

            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "templates", "teams-alarm-card.json"),
                temporaryPath,
                overwrite: true);
            viewModel.ReloadTemplateCommand.Execute(null);

            Assert.Contains("\"type\": \"AdaptiveCard\"", viewModel.PreviewText, StringComparison.Ordinal);
            Assert.Equal(Strings.TeamsStatusTemplateReloaded, viewModel.StatusMessage);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    [Theory]
    [InlineData(NotificationSendStatus.Succeeded, null, null)]
    [InlineData(NotificationSendStatus.RetryableFailure, "teams_http_error", 429)]
    [InlineData(NotificationSendStatus.PermanentFailure, "teams_http_error", 400)]
    public async Task SendAsync_DisplaysClassifiedResult(
        NotificationSendStatus status,
        string? errorCode,
        int? httpStatusCode)
    {
        var result = new NotificationSendResult(
            status,
            errorCode,
            httpStatusCode,
            status == NotificationSendStatus.Succeeded ? "request-1" : null);
        var service = new StubTeamsNotificationTestService
        {
            Handler = (_, _, _, _, _) => Task.FromResult(result),
        };
        var viewModel = CreateReadyViewModel(service: service);

        await viewModel.SendAsync(TestEndpoint);

        Assert.Equal(status, viewModel.LastSendStatus);
        Assert.Equal(errorCode, viewModel.LastErrorCode);
        Assert.Equal(httpStatusCode, viewModel.LastHttpStatusCode);
        Assert.Equal(result.ExternalMessageId, viewModel.LastExternalMessageId);
        Assert.NotNull(viewModel.ElapsedTime);
        Assert.DoesNotContain(TestEndpoint, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static TeamsNotificationTestViewModel CreateReadyViewModel(
        StubTeamsNotificationTestService? service = null,
        StubConfirmationService? confirmation = null,
        MutableTimeProvider? timeProvider = null,
        TeamsCardTemplateProvider? templateProvider = null)
    {
        var viewModel = CreateViewModel(service, confirmation, timeProvider, templateProvider);
        viewModel.SetWebhookUrlPresent(true);
        viewModel.IsRealSendConfirmed = true;
        return viewModel;
    }

    private static TeamsNotificationTestViewModel CreateViewModel(
        StubTeamsNotificationTestService? service = null,
        StubConfirmationService? confirmation = null,
        MutableTimeProvider? timeProvider = null,
        TeamsCardTemplateProvider? templateProvider = null) => new(
            templateProvider ?? CreateTemplateProvider(),
            service ?? new StubTeamsNotificationTestService(),
            confirmation ?? new StubConfirmationService(),
            timeProvider ?? CreateTimeProvider());

    private static TeamsCardTemplateProvider CreateTemplateProvider() => new(
        Path.Combine(AppContext.BaseDirectory, "templates", "teams-alarm-card.json"));

    private static MutableTimeProvider CreateTimeProvider() => new(
        new DateTimeOffset(2026, 10, 13, 8, 30, 0, TimeSpan.Zero),
        TimeZoneInfo.CreateCustomTimeZone(
            "Test Taipei",
            TimeSpan.FromHours(8),
            "Test Taipei",
            "Test Taipei"));

    private sealed class StubTeamsNotificationTestService : ITeamsNotificationTestService
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
                (_, _, _, _, _) => Task.FromResult(NotificationSendResult.Success("request-1"));

        public int CallCount { get; private set; }

        public string? WebhookUrl { get; private set; }

        public string? DestinationReference { get; private set; }

        public TimeSpan? Timeout { get; private set; }

        public AlarmNotification? Notification { get; private set; }

        public Task<NotificationSendResult> SendAsync(
            string webhookUrl,
            string destinationReference,
            TimeSpan timeout,
            AlarmNotification notification,
            CancellationToken cancellationToken)
        {
            CallCount++;
            WebhookUrl = webhookUrl;
            DestinationReference = destinationReference;
            Timeout = timeout;
            Notification = notification;
            return Handler(
                webhookUrl,
                destinationReference,
                timeout,
                notification,
                cancellationToken);
        }
    }

    private sealed class StubConfirmationService : ITeamsSendConfirmationService
    {
        public bool Result { get; init; } = true;

        public string MaskedEndpoint { get; private set; } = string.Empty;

        public string DestinationReference { get; private set; } = string.Empty;

        public bool Confirm(
            string maskedWebhookDestination,
            string destinationReference,
            Guid eventId,
            AlarmSeverity severity,
            string tag)
        {
            MaskedEndpoint = maskedWebhookDestination;
            DestinationReference = destinationReference;
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
