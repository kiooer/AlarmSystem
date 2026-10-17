namespace AlarmSystem.Application.Polling;

using AlarmSystem.Domain.Events;

public sealed record SourceAlarmEvent(
    Guid? SourceMessageId,
    DateTime? SourceEventTime,
    string? SourceNode,
    string? OperatorNode,
    string? TagName,
    string OriginalMessageType,
    string? AlarmStatus,
    string? RawPriority,
    string? Area,
    string? RawValue,
    string? Description,
    string? RawUnit);

public sealed record AlarmSourceReadRequest(
    DateTime LowerBoundInclusive,
    DateTime UpperBoundExclusive,
    DateTime? AfterTimestampExclusive,
    int MaxBatchSize);

public sealed record AlarmSourceReadResult(
    IReadOnlyList<SourceAlarmEvent> Events,
    bool HasMore,
    DateTime? OversizedTimestamp = null);

public static class AlarmPollingStreams
{
    public const string Alarm = "ALARM";
    public const string AckOperator = "ACK_OPERATOR";

    public static string GetMessageType(string streamName) => streamName switch
    {
        Alarm => "ALARM",
        AckOperator => "OPERATOR",
        _ => throw new ArgumentOutOfRangeException(
            nameof(streamName),
            streamName,
            "The polling stream is not supported."),
    };

    public static AlarmEventType GetEventType(string streamName) => streamName switch
    {
        Alarm => AlarmEventType.Alarm,
        AckOperator => AlarmEventType.Operator,
        _ => throw new ArgumentOutOfRangeException(
            nameof(streamName),
            streamName,
            "The polling stream is not supported."),
    };
}
