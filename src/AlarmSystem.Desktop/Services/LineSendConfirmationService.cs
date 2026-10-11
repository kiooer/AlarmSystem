using System.Globalization;
using System.Windows;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Desktop.Services;

public sealed class LineSendConfirmationService : ILineSendConfirmationService
{
    public bool Confirm(
        string maskedGroupId,
        Guid eventId,
        AlarmSeverity severity,
        string tag)
    {
        var severityDisplay = severity switch
        {
            AlarmSeverity.High => Strings.SeverityHigh,
            AlarmSeverity.Medium => Strings.SeverityMedium,
            AlarmSeverity.Low => Strings.SeverityLow,
            _ => Strings.NotAvailable,
        };
        var message = string.Format(
            CultureInfo.CurrentCulture,
            Strings.SendConfirmationMessageFormat,
            maskedGroupId,
            eventId.ToString("D"),
            severityDisplay,
            tag);

        return MessageBox.Show(
            message,
            Strings.SendConfirmationTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
