using AlarmSystem.Infrastructure.Notifications.Line;

namespace AlarmSystem.IntegrationTests.Notifications.Line;

public sealed class LineOptionsTests
{
    [Fact]
    public void Validate_AllowsDisabledChannelWithoutGroupIdOrToken()
    {
        var options = new LineOptions
        {
            Enabled = false,
            GroupId = string.Empty,
            ChannelAccessToken = string.Empty,
        };

        options.Validate();
    }

    [Fact]
    public void Validate_RejectsEnabledChannelWithoutToken()
    {
        var options = CreateEnabledOptions(channelAccessToken: "   ");

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("ChannelAccessToken", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsEnabledChannelWithoutGroupId()
    {
        var options = CreateEnabledOptions(groupId: "   ");

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("GroupId", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void Validate_RejectsInvalidTimeout(int timeoutSeconds)
    {
        var options = CreateEnabledOptions(timeout: TimeSpan.FromSeconds(timeoutSeconds));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Timeout", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DoesNotIncludeTokenInErrorMessage()
    {
        const string sensitiveToken = "credential-that-must-not-appear";
        var options = CreateEnabledOptions(
            groupId: string.Empty,
            channelAccessToken: sensitiveToken);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.False(
            exception.Message.Contains(sensitiveToken, StringComparison.Ordinal),
            "Validation error exposed the configured credential.");
    }

    [Fact]
    public void Validate_RejectsBlankTemplatePathWithoutExposingToken()
    {
        const string sensitiveToken = "credential-that-must-not-appear";
        var options = new LineOptions
        {
            Enabled = true,
            GroupId = "test-group",
            ChannelAccessToken = sensitiveToken,
            TemplatePath = " ",
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("TemplatePath", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveToken, exception.Message, StringComparison.Ordinal);
    }

    private static LineOptions CreateEnabledOptions(
        string groupId = "test-group",
        string channelAccessToken = "test-credential",
        TimeSpan? timeout = null) => new()
        {
            Enabled = true,
            GroupId = groupId,
            ChannelAccessToken = channelAccessToken,
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };
}
