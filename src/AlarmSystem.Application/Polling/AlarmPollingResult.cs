namespace AlarmSystem.Application.Polling;

public sealed record AlarmStreamPollingResult(
    string StreamName,
    bool UsedInitialLookback,
    DateTime QueryLowerBound,
    DateTime QueryUpperBound,
    int ReadCount,
    int InsertedCount,
    int DuplicateCount,
    int PageCount,
    int UnmappedPriorityCount,
    TimeSpan Elapsed);

public sealed record AlarmPollingResult(
    IReadOnlyList<AlarmStreamPollingResult> Streams,
    TimeSpan Elapsed)
{
    public int ReadCount => Streams.Sum(stream => stream.ReadCount);

    public int InsertedCount => Streams.Sum(stream => stream.InsertedCount);

    public int DuplicateCount => Streams.Sum(stream => stream.DuplicateCount);
}
