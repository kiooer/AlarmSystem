using AlarmSystem.Application.Polling;
using AlarmSystem.Domain.Events;

namespace AlarmSystem.Application.Abstractions.Persistence;

public interface IAlarmEventIngestionRepository
{
    Task<PollingCheckpoint?> GetCheckpointAsync(
        string sourceSystem,
        string streamName,
        CancellationToken cancellationToken = default);

    Task<AlarmEventIngestionResult> CommitBatchAsync(
        string sourceSystem,
        string streamName,
        IReadOnlyList<AlarmEventIngestionItem> events,
        DateTime watermarkSourceTime,
        Guid? lastProcessedMessageId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        string sourceSystem,
        string streamName,
        string errorCode,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record PollingCheckpoint(
    string SourceSystem,
    string StreamName,
    DateTime? WatermarkSourceTime,
    Guid? LastProcessedMessageId,
    DateTime? LastCompletedAtUtc,
    string? LastErrorCode);

public sealed record AlarmEventIngestionItem(
    Guid EventId,
    Guid SourceMessageId,
    string OriginalMessageType,
    AlarmEventType EventType,
    string? SourceNode,
    string? OperatorNode,
    string? TagName,
    string? AlarmStatus,
    string? AlarmLevel,
    string? Area,
    string? RawValue,
    string? Description,
    string? RawPriority,
    string? RawUnit,
    DateTime SourceEventTime,
    string SourceTimeZoneId,
    DateTime? EventTimeUtc,
    DateTime IngestedAtUtc,
    string AckMatchStatus,
    string? DataQualityFlags);

public sealed record AlarmEventIngestionResult(int InsertedCount, int DuplicateCount);
