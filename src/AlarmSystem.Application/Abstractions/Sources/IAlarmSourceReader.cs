using AlarmSystem.Application.Polling;

namespace AlarmSystem.Application.Abstractions.Sources;

public interface IAlarmSourceReader
{
    string StreamName { get; }

    Task<AlarmSourceReadResult> ReadAsync(
        AlarmSourceReadRequest request,
        CancellationToken cancellationToken = default);
}
