using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Notifications;

namespace AlarmSystem.Application.Tests.Licensing;

public sealed class CompositeLicenseStatusReaderTests
{
    [Fact]
    public async Task ReadStatusAsync_MergesPerFeatureDongleSoftwareAndDemoSources()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var reader = new CompositeLicenseStatusReader(
            new StubAuthorizer(feature => feature == NotificationFeature.Teams
                ? LicenseAuthorizationResult.Authorized
                : LicenseAuthorizationResult.Denied(
                    LicenseAuthorizationFailureCodes.FeatureNotLicensed)),
            new StubStatusReader(new LicenseStatus(
                LicenseSource.Software,
                LicenseState.Active,
                "SOFTWARE-1",
                "SUBSCRIPTION",
                now,
                now,
                now.AddDays(365),
                365,
                true,
                [NotificationFeature.Teams, NotificationFeature.Line],
                now,
                null,
                "active",
                FormatVersion: 2,
                BindingMode: "MACHINE",
                SignatureValid: true)),
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
        Assert.Equal(LicenseSource.Software, status.FeatureSources[NotificationFeature.Line]);
        Assert.Equal(LicenseSource.Demo, status.FeatureSources[NotificationFeature.Email]);
    }

    [Fact]
    public async Task ReadStatusAsync_SoftwareAndDemoUnionUsesSoftwareForOverlap()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var reader = new CompositeLicenseStatusReader(
            new StubAuthorizer(_ => LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.DongleUnavailable)),
            new StubStatusReader(CreateActive(
                LicenseSource.Software,
                "V2-1",
                [NotificationFeature.Line, NotificationFeature.Teams],
                now)),
            new StubStatusReader(CreateActive(
                LicenseSource.Demo,
                "V1-1",
                [NotificationFeature.Teams, NotificationFeature.Email],
                now)),
            new FixedTimeProvider(now));

        var status = await reader.ReadStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseSource.Composite, status.Source);
        Assert.Equal(LicenseSource.Software, status.FeatureSources![NotificationFeature.Teams]);
        Assert.Equal(LicenseSource.Software, status.FeatureSources[NotificationFeature.Line]);
        Assert.Equal(LicenseSource.Demo, status.FeatureSources[NotificationFeature.Email]);
    }

    [Fact]
    public async Task ReadStatusAsync_DongleValidationFailureBlocksAllFallback()
    {
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var active = new LicenseStatus(
            LicenseSource.Software,
            LicenseState.Active,
            "SOFTWARE-1",
            "SUBSCRIPTION",
            now,
            now,
            now.AddDays(30),
            30,
            true,
            Enum.GetValues<NotificationFeature>(),
            now,
            null,
            "active");
        var reader = new CompositeLicenseStatusReader(
            new StubAuthorizer(_ => LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.ValidationFailed)),
            new StubStatusReader(active),
            new StubStatusReader(active with { Source = LicenseSource.Demo }),
            new FixedTimeProvider(now));

        var status = await reader.ReadStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseSource.Dongle, status.Source);
        Assert.Equal(LicenseState.Invalid, status.State);
        Assert.Empty(status.AuthorizedFeatures);
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

    private static LicenseStatus CreateActive(
        LicenseSource source,
        string id,
        IReadOnlyList<NotificationFeature> features,
        DateTimeOffset now) => new(
            source,
            LicenseState.Active,
            id,
            source == LicenseSource.Software ? "SUBSCRIPTION" : "Demo",
            now,
            now,
            now.AddDays(30),
            30,
            true,
            features,
            now,
            null,
            "active");
}
