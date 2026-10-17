using AlarmSystem.Infrastructure.Licensing.Software;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class SoftwareLicenseOptionsTests
{
    [Fact]
    public void Validate_AllowsIndependentSecureDefaults()
    {
        var options = new SoftwareLicenseOptions();

        options.Validate();

        Assert.True(Path.IsPathFullyQualified(options.LicensePath));
        Assert.True(Path.IsPathFullyQualified(options.StatePath));
        Assert.EndsWith("software-v2.lic", options.LicensePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("software-license-state.dat", options.StatePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64 * 1024, options.MaximumLicenseBytes);
    }

    [Fact]
    public void Validate_RejectsSameLicenseAndStatePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "same-license-file.dat");
        var options = new SoftwareLicenseOptions { LicensePath = path, StatePath = path };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    public void Validate_RejectsInvalidRollbackTolerance(int seconds)
    {
        var defaults = new SoftwareLicenseOptions();
        var options = new SoftwareLicenseOptions
        {
            LicensePath = defaults.LicensePath,
            StatePath = defaults.StatePath,
            ClockRollbackTolerance = TimeSpan.FromSeconds(seconds),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
