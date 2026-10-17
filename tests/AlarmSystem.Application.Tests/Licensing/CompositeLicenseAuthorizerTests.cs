using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Notifications;

namespace AlarmSystem.Application.Tests.Licensing;

public sealed class CompositeLicenseAuthorizerTests
{
    [Fact]
    public async Task AuthorizeAsync_PrefersAuthorizedDongle()
    {
        var demoCalls = 0;
        var authorizer = new CompositeLicenseAuthorizer(
            new StubAuthorizer(_ => LicenseAuthorizationResult.Authorized),
            new StubAuthorizer(_ =>
            {
                demoCalls++;
                return LicenseAuthorizationResult.Authorized;
            }));

        var result = await authorizer.AuthorizeAsync(
            NotificationFeature.Teams,
            CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Equal(0, demoCalls);
    }

    [Theory]
    [InlineData(LicenseAuthorizationFailureCodes.DongleUnavailable)]
    [InlineData(LicenseAuthorizationFailureCodes.FeatureNotLicensed)]
    [InlineData(LicenseAuthorizationFailureCodes.Expired)]
    public async Task AuthorizeAsync_UsesDemoForNormalDongleDenial(string dongleFailure)
    {
        var authorizer = new CompositeLicenseAuthorizer(
            new StubAuthorizer(_ => LicenseAuthorizationResult.Denied(dongleFailure)),
            new StubAuthorizer(_ => LicenseAuthorizationResult.Authorized));

        var result = await authorizer.AuthorizeAsync(
            NotificationFeature.Line,
            CancellationToken.None);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task AuthorizeAsync_DoesNotFallbackAfterDongleSecurityFailure()
    {
        var demoCalls = 0;
        var authorizer = new CompositeLicenseAuthorizer(
            new StubAuthorizer(_ => LicenseAuthorizationResult.Denied(
                LicenseAuthorizationFailureCodes.ValidationFailed)),
            new StubAuthorizer(_ =>
            {
                demoCalls++;
                return LicenseAuthorizationResult.Authorized;
            }));

        var result = await authorizer.AuthorizeAsync(
            NotificationFeature.Email,
            CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal(LicenseAuthorizationFailureCodes.ValidationFailed, result.FailureCode);
        Assert.Equal(0, demoCalls);
    }

    private sealed class StubAuthorizer(
        Func<NotificationFeature, LicenseAuthorizationResult> handler)
        : INotificationLicenseAuthorizer
    {
        public Task<LicenseAuthorizationResult> AuthorizeAsync(
            NotificationFeature feature,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(feature));
        }
    }
}
