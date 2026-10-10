using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.IntegrationTests.Notifications.Teams;

public sealed class TeamsOptionsTests
{
    [Fact]
    public void Validate_AllowsDisabledChannelWithoutEndpoint()
    {
        var options = new TeamsOptions
        {
            Enabled = false,
            DestinationReference = "not-configured",
        };

        options.Validate();
    }

    [Theory]
    [InlineData("http://example.invalid/hook")]
    [InlineData("relative/path")]
    public void Validate_RejectsNonHttpsEndpointWhenEnabled(string endpoint)
    {
        var options = new TeamsOptions
        {
            Enabled = true,
            Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute),
            DestinationReference = "test-destination",
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
