using AlarmSystem.Domain.Events;

namespace AlarmSystem.Desktop.Services;

public interface ILineSendConfirmationService
{
    bool Confirm(
        string maskedGroupId,
        Guid eventId,
        AlarmSeverity severity,
        string tag);
}
