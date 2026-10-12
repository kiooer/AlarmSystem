using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class DongleLicenseOptionsTests
{
    [Fact]
    public void Validate_AllowsFailClosedUnconfiguredDefaults()
    {
        var options = new DongleLicenseOptions();

        options.Validate();

        Assert.All(
            Enum.GetValues<NotificationFeature>(),
            feature => Assert.True(options.FeatureMapping.ContainsKey(feature)));
        Assert.All(options.FeatureMapping.Values, Assert.Empty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void Validate_RejectsInvalidCheckTimeout(int timeoutSeconds)
    {
        var options = new DongleLicenseOptions
        {
            CheckTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("CheckTimeout", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizeAsync_FailsClosedWithoutVendorSdk()
    {
        var authorizer = new UnavailableDongleLicenseAuthorizer(new DongleLicenseOptions());

        foreach (var feature in Enum.GetValues<NotificationFeature>())
        {
            var result = await authorizer.AuthorizeAsync(feature, CancellationToken.None);

            Assert.False(result.IsAuthorized);
            Assert.Equal(LicenseAuthorizationFailureCodes.DongleUnavailable, result.FailureCode);
        }
    }

    [Fact]
    public async Task AuthorizeAsync_PropagatesCallerCancellation()
    {
        var authorizer = new UnavailableDongleLicenseAuthorizer(new DongleLicenseOptions());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => authorizer.AuthorizeAsync(NotificationFeature.Line, cancellation.Token));
    }
}
