using AlarmSystem.Application.Abstractions.Notifications;

namespace AlarmSystem.Desktop.Services;

public interface IEmailTestResultDialogService
{
    void ShowConnectionResult(
        string message,
        NotificationSendStatus? status);
}
