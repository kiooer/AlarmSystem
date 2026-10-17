namespace AlarmSystem.Application.Polling;

public sealed class AlarmPollingOptions
{
    public const string SectionName = "AlarmPolling";

    public bool Enabled { get; init; }

    public int IntervalSeconds { get; init; } = 5;

    public int OverlapSeconds { get; init; } = 60;

    public int MaxBatchSize { get; init; } = 500;

    public int CommandTimeoutSeconds { get; init; } = 10;

    public int InitialLookbackHours { get; init; } = 24;

    public int MaxFailureBackoffSeconds { get; init; } = 60;

    public string SourceTimeZoneId { get; init; } = "Taipei Standard Time";

    public string SourceConfigPath { get; init; } = string.Empty;

    public string AlarmStoreConfigPath { get; init; } = string.Empty;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (IntervalSeconds is < 1 or > 3600)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.IntervalSeconds must be between 1 and 3600.");
        }

        if (OverlapSeconds is < 0 or > 86400)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.OverlapSeconds must be between 0 and 86400.");
        }

        if (MaxBatchSize is < 1 or > 10000)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.MaxBatchSize must be between 1 and 10000.");
        }

        if (CommandTimeoutSeconds is < 1 or > 120)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.CommandTimeoutSeconds must be between 1 and 120.");
        }

        if (InitialLookbackHours is < 1 or > 8760)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.InitialLookbackHours must be between 1 and 8760.");
        }

        if (MaxFailureBackoffSeconds < IntervalSeconds || MaxFailureBackoffSeconds > 3600)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.MaxFailureBackoffSeconds must be at least IntervalSeconds and no more than 3600.");
        }

        if (string.IsNullOrWhiteSpace(SourceTimeZoneId))
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.SourceTimeZoneId is required when polling is enabled.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(SourceTimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.SourceTimeZoneId is not installed on this host.",
                exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.SourceTimeZoneId contains invalid time-zone rules.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(SourceConfigPath))
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.SourceConfigPath is required when polling is enabled.");
        }

        if (string.IsNullOrWhiteSpace(AlarmStoreConfigPath))
        {
            throw new AlarmPollingConfigurationException(
                "AlarmPolling.AlarmStoreConfigPath is required when polling is enabled.");
        }
    }
}

public sealed class AlarmPollingConfigurationException : Exception
{
    public AlarmPollingConfigurationException(string message)
        : base(message)
    {
    }

    public AlarmPollingConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
