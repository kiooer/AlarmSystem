using System.Windows;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Desktop.Resources;

namespace AlarmSystem.Desktop.Services;

public sealed class EmailTestResultDialogService : IEmailTestResultDialogService
{
    public void ShowConnectionResult(
        string message,
        NotificationSendStatus? status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        MessageBox.Show(
            message,
            Strings.EmailConnectionResultTitle,
            MessageBoxButton.OK,
            status == NotificationSendStatus.Succeeded
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);
    }
}
