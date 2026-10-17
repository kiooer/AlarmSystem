using System.Diagnostics;
using AlarmSystem.Application.Polling;

namespace AlarmSystem.Worker;

public class Worker(
    ILogger<Worker> logger,
    AlarmPollingOptions options,
    IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation(
                "Alarm polling is disabled. No Source or AlarmStore connection will be attempted.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        var pollingService = services.GetRequiredService<AlarmPollingService>();
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await pollingService.PollOnceAsync(stoppingToken);
                consecutiveFailures = 0;
                foreach (var stream in result.Streams)
                {
                    logger.LogInformation(
                        "Polling completed. Stream={Stream} InitialLookback={InitialLookback} Lower={Lower:O} Upper={Upper:O} Read={ReadCount} Inserted={InsertedCount} Duplicates={DuplicateCount} Pages={PageCount} UnmappedPriority={UnmappedPriorityCount} ElapsedMs={ElapsedMs}",
                        stream.StreamName,
                        stream.UsedInitialLookback,
                        stream.QueryLowerBound,
                        stream.QueryUpperBound,
                        stream.ReadCount,
                        stream.InsertedCount,
                        stream.DuplicateCount,
                        stream.PageCount,
                        stream.UnmappedPriorityCount,
                        stream.Elapsed.TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailures = Math.Min(consecutiveFailures + 1, 30);
                var errorCode = exception is AlarmPollingException pollingException
                    ? pollingException.ErrorCode
                    : "polling_unclassified_error";
                logger.LogError(
                    "Alarm polling cycle failed. ErrorCode={ErrorCode} ExceptionType={ExceptionType} ConsecutiveFailures={ConsecutiveFailures}",
                    errorCode,
                    exception.GetType().FullName,
                    consecutiveFailures);
            }

            var normalDelay = TimeSpan.FromSeconds(options.IntervalSeconds);
            var delay = consecutiveFailures == 0
                ? normalDelay
                : TimeSpan.FromSeconds(Math.Min(
                    options.MaxFailureBackoffSeconds,
                    options.IntervalSeconds * Math.Pow(2, Math.Min(consecutiveFailures, 10))));
            logger.LogDebug(
                "Alarm polling cycle finished in {ElapsedMs} ms; next attempt in {DelaySeconds} seconds.",
                stopwatch.Elapsed.TotalMilliseconds,
                delay.TotalSeconds);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
