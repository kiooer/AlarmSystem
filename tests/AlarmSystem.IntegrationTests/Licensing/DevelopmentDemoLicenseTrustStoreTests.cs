using AlarmSystem.Infrastructure.Licensing.Demo;

namespace AlarmSystem.IntegrationTests.Licensing;

public sealed class DevelopmentDemoLicenseTrustStoreTests
{
    private const string LocalUiKeyId = "dev-ui-20261017";

#if DEBUG
    [Fact]
    public void Create_DebugDevelopment_TrustsLocalUiKey()
    {
        var trustStore = DevelopmentDemoLicenseTrustStore.Create(isDevelopment: true);

        var found = trustStore.TryGetPublicKey(LocalUiKeyId, out var publicKey);

        Assert.True(found);
        Assert.False(publicKey.IsEmpty);
    }
#endif

    [Fact]
    public void Create_NonDevelopment_DoesNotTrustLocalUiKey()
    {
        var trustStore = DevelopmentDemoLicenseTrustStore.Create(isDevelopment: false);

        var found = trustStore.TryGetPublicKey(LocalUiKeyId, out _);

        Assert.False(found);
    }
}
