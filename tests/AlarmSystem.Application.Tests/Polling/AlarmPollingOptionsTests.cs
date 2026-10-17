using AlarmSystem.Application.Polling;

namespace AlarmSystem.Application.Tests.Polling;

public sealed class AlarmPollingOptionsTests
{
    [Fact]
    public void Disabled_AllowsMissingPrivateConfigurationPaths()
    {
        new AlarmPollingOptions { Enabled = false }.Validate();
    }

    [Fact]
    public void Enabled_RequiresPathsAndValidRanges()
    {
        var missingPaths = ValidOptions().WithChanges(sourcePath: string.Empty);
        Assert.Throws<AlarmPollingConfigurationException>(() => missingPaths.Validate());

        var invalidBatch = ValidOptions().WithChanges(maxBatchSize: 0);
        Assert.Throws<AlarmPollingConfigurationException>(() => invalidBatch.Validate());
    }

    private static AlarmPollingOptions ValidOptions() => new()
    {
        Enabled = true,
        IntervalSeconds = 5,
        OverlapSeconds = 60,
        MaxBatchSize = 500,
        CommandTimeoutSeconds = 10,
        InitialLookbackHours = 24,
        MaxFailureBackoffSeconds = 60,
        SourceTimeZoneId = "Taipei Standard Time",
        SourceConfigPath = "source.json",
        AlarmStoreConfigPath = "store.json",
    };
}

internal static class AlarmPollingOptionsTestExtensions
{
    public static AlarmPollingOptions WithChanges(
        this AlarmPollingOptions options,
        string? sourcePath = null,
        int? maxBatchSize = null) => new()
        {
            Enabled = options.Enabled,
            IntervalSeconds = options.IntervalSeconds,
            OverlapSeconds = options.OverlapSeconds,
            MaxBatchSize = maxBatchSize ?? options.MaxBatchSize,
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            InitialLookbackHours = options.InitialLookbackHours,
            MaxFailureBackoffSeconds = options.MaxFailureBackoffSeconds,
            SourceTimeZoneId = options.SourceTimeZoneId,
            SourceConfigPath = sourcePath ?? options.SourceConfigPath,
            AlarmStoreConfigPath = options.AlarmStoreConfigPath,
        };
}
