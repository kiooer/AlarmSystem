namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed class DemoLicenseOptions
{
    public const string SectionName = "Licensing:Demo";
    public const int DefaultMaximumLicenseBytes = 64 * 1024;
    public const string DefaultPipeName = "AlarmSystem.LicenseManagement.v1";

    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AlarmSystem",
        "Licensing");

    public string LicensePath { get; init; } = Path.Combine(DefaultDirectory, "demo.lic");

    public string StatePath { get; init; } = Path.Combine(DefaultDirectory, "license-state.dat");

    public TimeSpan ClockRollbackTolerance { get; init; } = TimeSpan.FromMinutes(2);

    public int MaximumLicenseBytes { get; init; } = DefaultMaximumLicenseBytes;

    public string PipeName { get; init; } = DefaultPipeName;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(LicensePath) || !Path.IsPathFullyQualified(LicensePath))
        {
            throw new InvalidOperationException($"{SectionName}:LicensePath must be an absolute path.");
        }

        if (string.IsNullOrWhiteSpace(StatePath) || !Path.IsPathFullyQualified(StatePath))
        {
            throw new InvalidOperationException($"{SectionName}:StatePath must be an absolute path.");
        }

        if (Path.GetFullPath(LicensePath).Equals(
            Path.GetFullPath(StatePath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{SectionName}:LicensePath and StatePath must be different files.");
        }

        if (ClockRollbackTolerance < TimeSpan.Zero ||
            ClockRollbackTolerance > TimeSpan.FromMinutes(10))
        {
            throw new InvalidOperationException(
                $"{SectionName}:ClockRollbackTolerance must be between zero and ten minutes.");
        }

        if (MaximumLicenseBytes is < 1024 or > DefaultMaximumLicenseBytes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaximumLicenseBytes must be between 1024 and {DefaultMaximumLicenseBytes}.");
        }

        if (string.IsNullOrWhiteSpace(PipeName) || PipeName.Length > 128 ||
            PipeName.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new InvalidOperationException($"{SectionName}:PipeName is invalid.");
        }
    }
}
