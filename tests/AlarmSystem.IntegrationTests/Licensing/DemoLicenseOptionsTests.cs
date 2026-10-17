using AlarmSystem.Infrastructure.Licensing.Demo;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class DemoLicenseOptionsTests
{
    [Fact]
    public void Validate_AllowsSecureDefaults()
    {
        var options = new DemoLicenseOptions();

        options.Validate();

        Assert.True(Path.IsPathFullyQualified(options.LicensePath));
        Assert.True(Path.IsPathFullyQualified(options.StatePath));
        Assert.Equal(64 * 1024, options.MaximumLicenseBytes);
        Assert.Equal(TimeSpan.FromMinutes(2), options.ClockRollbackTolerance);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    public void Validate_RejectsInvalidClockRollbackTolerance(int seconds)
    {
        var defaults = new DemoLicenseOptions();
        var options = new DemoLicenseOptions
        {
            LicensePath = defaults.LicensePath,
            StatePath = defaults.StatePath,
            ClockRollbackTolerance = TimeSpan.FromSeconds(seconds),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(65537)]
    public void Validate_RejectsInvalidMaximumFileSize(int bytes)
    {
        var defaults = new DemoLicenseOptions();
        var options = new DemoLicenseOptions
        {
            LicensePath = defaults.LicensePath,
            StatePath = defaults.StatePath,
            MaximumLicenseBytes = bytes,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
