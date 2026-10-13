using System.IO;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;
using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.Desktop.Tests.ViewModels;

public sealed class EmailNotificationTestViewModelTests
{
    [Fact]
    public void ManualTestNotice_ExplainsDesktopOnlyLicenseException()
    {
        Assert.Contains("手動測試", Strings.EmailToolNotice, StringComparison.Ordinal);
        Assert.Contains("不檢查 Dongle License", Strings.EmailToolNotice, StringComparison.Ordinal);
        Assert.Contains("不代表正式自動發報已取得授權", Strings.EmailToolNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ProducesHtmlAndPlainTextPreview()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(Strings.EmailPreviewSubjectLabel, viewModel.PreviewText);
        Assert.Contains(Strings.EmailPreviewPlainTextLabel, viewModel.PreviewText);
        Assert.Contains(Strings.EmailPreviewHtmlLabel, viewModel.PreviewText);
        Assert.Contains("TEST.TAG", viewModel.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionButtons_AreAvailableSoValidationCanExplainMissingSettings()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.CanTestConnection);
        Assert.True(viewModel.CanSend);
    }

    [Fact]
    public async Task TestConnectionAsync_UsesConnectionOperationOnly()
    {
        var service = new StubEmailNotificationTestService();
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateConnectionReadyViewModel(service, resultDialog);
        viewModel.SmtpPortText = "465";
        viewModel.SelectedSecurityMode = EmailSecurityMode.SslTls;

        await viewModel.TestConnectionAsync("desktop-test-password");

        Assert.Equal(1, service.ConnectionCallCount);
        Assert.Equal(0, service.SendCallCount);
        Assert.Empty(service.ConnectionOptions!.Recipients);
        Assert.Empty(service.ConnectionOptions.SenderAddress);
        Assert.Equal(465, service.ConnectionOptions.Port);
        Assert.Equal(EmailSecurityMode.SslTls, service.ConnectionOptions.SecurityMode);
        Assert.Equal(Strings.EmailConnectionSucceeded, viewModel.StatusMessage);
        Assert.Equal(1, resultDialog.CallCount);
        Assert.Equal(NotificationSendStatus.Succeeded, resultDialog.Status);
        Assert.Equal(Strings.EmailConnectionSucceeded, resultDialog.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_WithoutAuthenticationPassesTlsSettingsWithoutPassword()
    {
        var service = new StubEmailNotificationTestService();
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateConnectionReadyViewModel(service, resultDialog);
        viewModel.SmtpHost = "smtp.gmail.com";
        viewModel.SmtpPortText = "587";
        viewModel.SelectedSecurityMode = EmailSecurityMode.StartTls;
        viewModel.SelectedAuthenticationMode = EmailAuthenticationMode.None;
        viewModel.Username = string.Empty;

        await viewModel.TestConnectionAsync(string.Empty);

        Assert.Equal(1, service.ConnectionCallCount);
        Assert.Equal("smtp.gmail.com", service.ConnectionOptions!.Host);
        Assert.Equal(587, service.ConnectionOptions.Port);
        Assert.Equal(EmailSecurityMode.StartTls, service.ConnectionOptions.SecurityMode);
        Assert.Equal(
            EmailAuthenticationMode.None,
            service.ConnectionOptions.AuthenticationMode);
        Assert.Equal(NotificationSendStatus.Succeeded, viewModel.LastSendStatus);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenSmtpFailsShowsFailureDialog()
    {
        var service = new StubEmailNotificationTestService
        {
            ConnectionResult = NotificationSendResult.Retryable("email_timeout"),
        };
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateConnectionReadyViewModel(service, resultDialog);

        await viewModel.TestConnectionAsync("desktop-test-password");

        Assert.Equal(1, resultDialog.CallCount);
        Assert.Equal(NotificationSendStatus.RetryableFailure, resultDialog.Status);
        Assert.Contains("email_timeout", resultDialog.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenTransportFailsShowsSafeStageDiagnostic()
    {
        var correlationId = Guid.Parse("9db73cce-d805-4e93-b353-4a08fb1171da");
        var service = new StubEmailNotificationTestService
        {
            ConnectionExecution = new EmailSmtpExecutionResult(
                NotificationSendResult.Retryable("email_smtp_transport_error"),
                [new EmailSmtpStageDiagnostic(
                    correlationId,
                    EmailSmtpStage.Connect,
                    EmailSmtpStageResult.Failed,
                    "email_smtp_transport_error",
                    "SocketException",
                    EmailSmtpFailureKind.Transport,
                    null,
                    System.Net.Sockets.SocketError.ConnectionRefused,
                    125,
                    EmailSecurityMode.StartTls,
                    Ipv4Address: "192.0.2.20")]),
        };
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateConnectionReadyViewModel(service, resultDialog);

        await viewModel.TestConnectionAsync("desktop-test-password");

        Assert.Contains(Strings.EmailSmtpStageConnect, viewModel.LastSmtpStageDisplay);
        Assert.Contains(Strings.EmailDiagnosticTransport, viewModel.LastDiagnosticDetailDisplay);
        Assert.Contains("SocketException", viewModel.LastDiagnosticDetailDisplay);
        Assert.Contains("192.0.2.20", viewModel.LastDiagnosticDetailDisplay);
        Assert.Equal(correlationId.ToString("D"), viewModel.LastCorrelationIdDisplay);
        Assert.Contains(Strings.EmailSmtpStageConnect, resultDialog.Message);
        Assert.DoesNotContain("desktop-test-password", resultDialog.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenDisconnectWarnsStillShowsSuccess()
    {
        var service = new StubEmailNotificationTestService
        {
            ConnectionExecution = new EmailSmtpExecutionResult(
                NotificationSendResult.Success(),
                [new EmailSmtpStageDiagnostic(
                    Guid.NewGuid(),
                    EmailSmtpStage.Disconnect,
                    EmailSmtpStageResult.Warning,
                    "email_smtp_disconnect_warning",
                    "IOException",
                    EmailSmtpFailureKind.Transport,
                    null,
                    null,
                    20,
                    EmailSecurityMode.StartTls)]),
        };
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateConnectionReadyViewModel(service, resultDialog);

        await viewModel.TestConnectionAsync("desktop-test-password");

        Assert.Equal(NotificationSendStatus.Succeeded, viewModel.LastSendStatus);
        Assert.Equal("email_smtp_disconnect_warning", viewModel.LastWarningCode);
        Assert.Contains(Strings.EmailSmtpStageDisconnect, viewModel.StatusMessage);
        Assert.Contains("email_smtp_disconnect_warning", resultDialog.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenValidationFailsShowsDialogWithoutCallingService()
    {
        var service = new StubEmailNotificationTestService();
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateViewModel(service, resultDialog: resultDialog);

        await viewModel.TestConnectionAsync(string.Empty);

        Assert.Equal(0, service.ConnectionCallCount);
        Assert.Equal(1, resultDialog.CallCount);
        Assert.Null(resultDialog.Status);
        Assert.Equal(Strings.EmailValidationPasswordRequired, resultDialog.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_ValidationFailureClearsPreviousSuccessStatus()
    {
        var service = new StubEmailNotificationTestService();
        var resultDialog = new StubResultDialogService();
        var viewModel = CreateConnectionReadyViewModel(service, resultDialog);
        await viewModel.TestConnectionAsync("desktop-test-password");
        viewModel.SmtpHost = string.Empty;

        await viewModel.TestConnectionAsync("desktop-test-password");

        Assert.Equal(1, service.ConnectionCallCount);
        Assert.Equal(2, resultDialog.CallCount);
        Assert.Null(resultDialog.Status);
        Assert.Equal(Strings.EmailValidationConnectionSettings, resultDialog.Message);
    }

    [Fact]
    public async Task SendAsync_WithMissingSettingsExplainsValidationAndDoesNotCallService()
    {
        var service = new StubEmailNotificationTestService();
        var viewModel = CreateViewModel(service);

        await viewModel.SendAsync(string.Empty);

        Assert.Equal(0, service.SendCallCount);
        Assert.Equal(Strings.EmailValidationPasswordRequired, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SendAsync_UsesAlarmAndRequiresConfirmationDialog()
    {
        var service = new StubEmailNotificationTestService();
        var confirmation = new StubConfirmationService();
        var viewModel = CreateReadyViewModel(service, confirmation);

        await viewModel.SendAsync("desktop-test-password");

        Assert.Equal(1, service.SendCallCount);
        Assert.Equal(AlarmSystem.Domain.Events.AlarmEventType.Alarm, service.Notification!.EventType);
        Assert.Equal("Operations", confirmation.DestinationReference);
        Assert.Equal(2, confirmation.RecipientCount);
        Assert.Equal(Strings.EmailSendSucceeded, viewModel.StatusMessage);
        Assert.False(viewModel.IsRealSendConfirmed);
    }

    [Fact]
    public async Task SendAsync_DialogCancellationDoesNotCallService()
    {
        var service = new StubEmailNotificationTestService();
        var confirmation = new StubConfirmationService { Result = false };
        var viewModel = CreateReadyViewModel(service, confirmation);

        await viewModel.SendAsync("desktop-test-password");

        Assert.Equal(0, service.SendCallCount);
        Assert.Equal(Strings.EmailStatusConfirmationCancelled, viewModel.StatusMessage);
    }

    [Fact]
    public void ViewModel_DoesNotExposeBindablePasswordStringProperty()
    {
        var passwordProperties = typeof(EmailNotificationTestViewModel)
            .GetProperties()
            .Where(property =>
                property.PropertyType == typeof(string) &&
                property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(passwordProperties);
    }

    private static EmailNotificationTestViewModel CreateReadyViewModel(
        StubEmailNotificationTestService? service = null,
        StubConfirmationService? confirmation = null)
    {
        var viewModel = CreateViewModel(service, confirmation);
        viewModel.SmtpHost = "smtp.example.invalid";
        viewModel.SmtpPortText = "587";
        viewModel.SelectedSecurityMode = EmailSecurityMode.StartTls;
        viewModel.SelectedAuthenticationMode = EmailAuthenticationMode.Password;
        viewModel.Username = "smtp-user";
        viewModel.SenderAddress = "alarm@example.invalid";
        viewModel.RecipientsText = "one@example.invalid; two@example.invalid";
        viewModel.DestinationReference = "Operations";
        viewModel.IsRealSendConfirmed = true;
        return viewModel;
    }

    private static EmailNotificationTestViewModel CreateConnectionReadyViewModel(
        StubEmailNotificationTestService? service = null,
        StubResultDialogService? resultDialog = null)
    {
        var viewModel = CreateViewModel(service, resultDialog: resultDialog);
        viewModel.SmtpHost = "smtp.example.invalid";
        viewModel.SmtpPortText = "587";
        viewModel.SelectedSecurityMode = EmailSecurityMode.StartTls;
        viewModel.SelectedAuthenticationMode = EmailAuthenticationMode.Password;
        viewModel.Username = "smtp-user";
        return viewModel;
    }

    private static EmailNotificationTestViewModel CreateViewModel(
        StubEmailNotificationTestService? service = null,
        StubConfirmationService? confirmation = null,
        StubResultDialogService? resultDialog = null)
    {
        var builder = new EmailMessageBuilder();
        return new EmailNotificationTestViewModel(
            new EmailTemplateProvider(
                Path.Combine(AppContext.BaseDirectory, "templates", "email-alarm.html"),
                builder),
            service ?? new StubEmailNotificationTestService(),
            confirmation ?? new StubConfirmationService(),
            resultDialog ?? new StubResultDialogService(),
            new FixedTimeProvider());
    }

    private sealed class StubEmailNotificationTestService : IEmailNotificationTestService
    {
        public NotificationSendResult ConnectionResult { get; init; } =
            NotificationSendResult.Success();

        public EmailSmtpExecutionResult? ConnectionExecution { get; init; }

        public int ConnectionCallCount { get; private set; }

        public int SendCallCount { get; private set; }

        public AlarmNotification? Notification { get; private set; }

        public EmailOptions? ConnectionOptions { get; private set; }

        public Task<EmailSmtpExecutionResult> TestConnectionAsync(
            EmailOptions options,
            CancellationToken cancellationToken)
        {
            ConnectionCallCount++;
            ConnectionOptions = options;
            return Task.FromResult(
                ConnectionExecution ??
                EmailSmtpExecutionResult.WithoutDiagnostics(ConnectionResult));
        }

        public Task<EmailSmtpExecutionResult> SendAsync(
            EmailOptions options,
            AlarmNotification notification,
            CancellationToken cancellationToken)
        {
            SendCallCount++;
            Notification = notification;
            return Task.FromResult(
                EmailSmtpExecutionResult.WithoutDiagnostics(
                    NotificationSendResult.Success()));
        }
    }

    private sealed class StubResultDialogService : IEmailTestResultDialogService
    {
        public int CallCount { get; private set; }

        public string Message { get; private set; } = string.Empty;

        public NotificationSendStatus? Status { get; private set; }

        public void ShowConnectionResult(
            string message,
            NotificationSendStatus? status)
        {
            CallCount++;
            Message = message;
            Status = status;
        }
    }

    private sealed class StubConfirmationService : IEmailSendConfirmationService
    {
        public bool Result { get; init; } = true;

        public string? DestinationReference { get; private set; }

        public int RecipientCount { get; private set; }

        public bool Confirm(
            string destinationReference,
            string senderAddress,
            int recipientCount,
            Guid eventId,
            string tag)
        {
            DestinationReference = destinationReference;
            RecipientCount = recipientCount;
            return Result;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "Test Taipei",
            TimeSpan.FromHours(8),
            "Test Taipei",
            "Test Taipei");

        public override DateTimeOffset GetUtcNow() =>
            new(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);
    }
}
