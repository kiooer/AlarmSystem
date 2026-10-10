using AlarmSystem.Infrastructure.Notifications.Teams;

namespace AlarmSystem.IntegrationTests.Notifications.Teams;

public sealed class TeamsCardTemplateProviderTests
{
    [Fact]
    public void Constructor_MarksMissingTemplateUnhealthyWithoutThrowing()
    {
        var provider = new TeamsCardTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "missing-template.json"));

        Assert.False(provider.IsHealthy);
        Assert.Equal("teams_template_unhealthy", provider.FailureCode);
    }

    [Fact]
    public void Reload_RecoversAfterTemplateIsCorrected()
    {
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"alarm-system-teams-template-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(temporaryPath, "{invalid-json}");
            var provider = new TeamsCardTemplateProvider(temporaryPath);
            Assert.False(provider.IsHealthy);

            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "templates", "teams-alarm-card.json"),
                temporaryPath,
                overwrite: true);
            provider.Reload();

            Assert.True(provider.IsHealthy);
            Assert.Null(provider.FailureCode);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
