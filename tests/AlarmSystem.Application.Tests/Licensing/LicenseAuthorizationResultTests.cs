using AlarmSystem.Application.Abstractions.Licensing;

namespace AlarmSystem.Application.Tests.Licensing;

public sealed class LicenseAuthorizationResultTests
{
    [Fact]
    public void Authorized_HasNoFailureCode()
    {
        var result = LicenseAuthorizationResult.Authorized;

        Assert.True(result.IsAuthorized);
        Assert.Null(result.FailureCode);
    }

    [Theory]
    [InlineData(LicenseAuthorizationFailureCodes.FeatureNotLicensed)]
    [InlineData(LicenseAuthorizationFailureCodes.Expired)]
    [InlineData(LicenseAuthorizationFailureCodes.DongleUnavailable)]
    [InlineData(LicenseAuthorizationFailureCodes.ValidationFailed)]
    public void Denied_AcceptsOnlyControlledFailureCodes(string failureCode)
    {
        var result = LicenseAuthorizationResult.Denied(failureCode);

        Assert.False(result.IsAuthorized);
        Assert.Equal(failureCode, result.FailureCode);
    }

    [Fact]
    public void Denied_RejectsUnknownFailureCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LicenseAuthorizationResult.Denied("vendor-sensitive-error"));
    }

    [Fact]
    public void NotificationFeatures_AreStableAndIncludeReservedEmailFeature()
    {
        Assert.Equal(
            [NotificationFeature.Line, NotificationFeature.Teams, NotificationFeature.Email],
            Enum.GetValues<NotificationFeature>());
    }
}
