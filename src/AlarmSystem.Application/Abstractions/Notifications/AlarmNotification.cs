using AlarmSystem.Domain.Events;

namespace AlarmSystem.Application.Abstractions.Notifications;

public sealed record AlarmNotification(
    Guid EventId,
    string Tag,
    string? Area,
    AlarmEventType EventType,
    AlarmSeverity Severity,
    string Message,
    DateTimeOffset SourceTimestamp,
    string? Equipment = null);
