using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Notifications;

namespace AlarmSystem.Application.Tests.Licensing;

public sealed class CompositeLicenseStatusReaderTests
{
    [Fact]
    public async Task ReadStatusAsync_MergesPerFeatureDongleAndDemoSources()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var reader = new CompositeLicenseStatusReader(
            new StubAuthorizer(feature => feature == NotificationFeature.Teams
                ? LicenseAuthorizationResult.Authorized
                : LicenseAuthorizationResult.Denied(
                    LicenseAuthorizationFailureCodes.FeatureNotLicensed)),
            new StubStatusReader(new LicenseStatus(
                LicenseSource.Demo,
                LicenseState.Active,
                "DEMO-1",
                "Demo",
                now,
                now,
                now.AddDays(15),
                15,
                true,
                [NotificationFeature.Line, NotificationFeature.Email],
                now,
                null,
                "active")),
            new FixedTimeProvider(now));

        var status = await reader.ReadStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseSource.Composite, status.Source);
        Assert.Equal(Enum.GetValues<NotificationFeature>(), status.AuthorizedFeatures);
        Assert.Equal(LicenseSource.Dongle, status.FeatureSources![NotificationFeature.Teams]);
        Assert.Equal(LicenseSource.Demo, status.FeatureSources[NotificationFeature.Line]);
        Assert.Equal(LicenseSource.Demo, status.FeatureSources[NotificationFeature.Email]);
    }

    private sealed class StubAuthorizer(
        Func<NotificationFeature, LicenseAuthorizationResult> handler)
        : INotificationLicenseAuthorizer
    {
        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken) => Task.FromResult(handler(feature));
    }

    private sealed class StubStatusReader(LicenseStatus status) : ILicenseStatusReader
    {
        public Task<LicenseStatus> ReadStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
