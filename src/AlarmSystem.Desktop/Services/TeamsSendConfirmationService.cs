using System.Globalization;
using System.Windows;
using AlarmSystem.Desktop.Resources;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Desktop.Services;

public sealed class TeamsSendConfirmationService : ITeamsSendConfirmationService
{
    public bool Confirm(
        string maskedWebhookDestination,
        string destinationReference,
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
            Strings.TeamsSendConfirmationMessageFormat,
            maskedWebhookDestination,
            destinationReference,
            eventId.ToString("D"),
            severityDisplay,
            tag);

        return MessageBox.Show(
            message,
            Strings.TeamsSendConfirmationTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
