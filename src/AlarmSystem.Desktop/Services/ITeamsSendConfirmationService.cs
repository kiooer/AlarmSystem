using AlarmSystem.Domain.Events;

namespace AlarmSystem.Desktop.Services;

public interface ITeamsSendConfirmationService
{
    bool Confirm(
        string maskedWebhookDestination,
        string destinationReference,
        Guid eventId,
        AlarmSeverity severity,
        string tag);
}
