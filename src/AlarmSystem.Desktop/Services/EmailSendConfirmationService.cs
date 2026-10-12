using System.Globalization;
using System.Windows;
using AlarmSystem.Desktop.Resources;

namespace AlarmSystem.Desktop.Services;

public sealed class EmailSendConfirmationService : IEmailSendConfirmationService
{
    public bool Confirm(
        string destinationReference,
        string senderAddress,
        int recipientCount,
        Guid eventId,
        string tag)
    {
        var message = string.Format(
            CultureInfo.CurrentCulture,
            Strings.EmailSendConfirmationMessageFormat,
            destinationReference,
            senderAddress,
            recipientCount,
            eventId.ToString("D"),
            tag);
        return MessageBox.Show(
            message,
            Strings.EmailSendConfirmationTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
