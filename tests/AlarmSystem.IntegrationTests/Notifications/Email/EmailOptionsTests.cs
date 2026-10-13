using AlarmSystem.Infrastructure.Notifications.Email;

namespace AlarmSystem.IntegrationTests.Notifications.Email;

public sealed class EmailOptionsTests
{
    [Fact]
    public void Validate_AllowsDisabledChannelWithoutSmtpConfiguration()
    {
        var options = new EmailOptions { Enabled = false };

        options.Validate();
    }

    [Fact]
    public void Validate_AcceptsPasswordAuthenticatedStartTlsConfiguration()
    {
        var options = CreateEnabledOptions();

        options.Validate();
    }

    [Fact]
    public void ConfigurationMode_DefaultsToAdvancedForLegacySettings()
    {
        var options = new EmailOptions();

        Assert.Equal(EmailSmtpConfigurationMode.Advanced, options.ConfigurationMode);
    }

    [Fact]
    public void Validate_LegacyCustomPortWithoutConfigurationModeRemainsValid()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            Host = "smtp.example.invalid",
            Port = 2525,
            SecurityMode = EmailSecurityMode.StartTls,
            AuthenticationMode = EmailAuthenticationMode.Password,
            Username = "smtp-user",
            Password = "unit-test-password",
            SenderAddress = "alarm@example.invalid",
            Recipients = ["operator@example.invalid"],
            DestinationReference = "Operations",
        };

        options.Validate();

        Assert.Equal(EmailSmtpConfigurationMode.Advanced, options.ConfigurationMode);
    }

    [Fact]
    public void ValidateSmtpConnection_DoesNotRequireMessageAddressesOrDestination()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            Host = "smtp.example.invalid",
            Port = 587,
            SecurityMode = EmailSecurityMode.StartTls,
            AuthenticationMode = EmailAuthenticationMode.Password,
            Username = "smtp-user",
            Password = "unit-test-password",
        };

        options.ValidateSmtpConnection();
    }

    [Theory]
    [InlineData(465, EmailSecurityMode.SslTls)]
    [InlineData(587, EmailSecurityMode.StartTls)]
    public void Validate_AcceptsStandardPortSecurityMapping(
        int port,
        EmailSecurityMode securityMode)
    {
        var options = CreateEnabledOptions(
            configurationMode: EmailSmtpConfigurationMode.Standard,
            port: port,
            securityMode: securityMode);

        options.Validate();
    }

    [Theory]
    [InlineData(587, EmailSecurityMode.SslTls)]
    [InlineData(465, EmailSecurityMode.StartTls)]
    [InlineData(587, EmailSecurityMode.None)]
    public void Validate_RejectsIncompatibleStandardPortSecurityMapping(
        int port,
        EmailSecurityMode securityMode)
    {
        var options = CreateEnabledOptions(
            configurationMode: EmailSmtpConfigurationMode.Standard,
            port: port,
            securityMode: securityMode,
            authenticationMode: EmailAuthenticationMode.None,
            allowUnencryptedInternalRelay: securityMode == EmailSecurityMode.None,
            username: string.Empty,
            password: string.Empty);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsCustomPortInStandardMode()
    {
        var options = CreateEnabledOptions(
            configurationMode: EmailSmtpConfigurationMode.Standard,
            port: 2525);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_AcceptsCustomPortInAdvancedMode()
    {
        var options = CreateEnabledOptions(
            configurationMode: EmailSmtpConfigurationMode.Advanced,
            port: 2525,
            securityMode: EmailSecurityMode.StartTls);

        options.Validate();
    }

    [Fact]
    public void Validate_RejectsUnencryptedRelayFlagInStandardMode()
    {
        var options = CreateEnabledOptions(
            configurationMode: EmailSmtpConfigurationMode.Standard,
            allowUnencryptedInternalRelay: true);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_AcceptsExplicitUnauthenticatedInternalRelay()
    {
        var options = CreateEnabledOptions(
            securityMode: EmailSecurityMode.None,
            authenticationMode: EmailAuthenticationMode.None,
            allowUnencryptedInternalRelay: true,
            username: string.Empty,
            password: string.Empty);

        options.Validate();
    }

    [Fact]
    public void Validate_RejectsImplicitUnencryptedRelay()
    {
        var options = CreateEnabledOptions(
            securityMode: EmailSecurityMode.None,
            authenticationMode: EmailAuthenticationMode.None,
            allowUnencryptedInternalRelay: false,
            username: string.Empty,
            password: string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("AllowUnencryptedInternalRelay", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsPasswordAuthenticationWithoutEncryption()
    {
        var options = CreateEnabledOptions(
            securityMode: EmailSecurityMode.None,
            allowUnencryptedInternalRelay: true);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Password authentication", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validate_RejectsInvalidPort(int port)
    {
        var options = CreateEnabledOptions(port: port);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Validate_RejectsInvalidTimeout(int seconds)
    {
        var options = CreateEnabledOptions(timeout: TimeSpan.FromSeconds(seconds));

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-address")]
    public void Validate_RejectsInvalidSenderAddress(string senderAddress)
    {
        var options = CreateEnabledOptions(senderAddress: senderAddress);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsInvalidRecipientWithoutExposingAddressesOrPassword()
    {
        const string password = "test-password-that-must-not-be-exposed";
        const string recipient = "invalid-recipient";
        var options = CreateEnabledOptions(
            password: password,
            recipients: [recipient]);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(recipient, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RequiresPasswordCredentialsWhenSelectedWithoutExposingPassword()
    {
        var options = CreateEnabledOptions(password: string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Username and Password", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp-user", exception.Message, StringComparison.Ordinal);
    }

    private static EmailOptions CreateEnabledOptions(
        EmailSmtpConfigurationMode configurationMode = EmailSmtpConfigurationMode.Advanced,
        int port = 587,
        EmailSecurityMode securityMode = EmailSecurityMode.StartTls,
        EmailAuthenticationMode authenticationMode = EmailAuthenticationMode.Password,
        bool allowUnencryptedInternalRelay = false,
        string username = "smtp-user",
        string password = "unit-test-password",
        string senderAddress = "alarm@example.invalid",
        IReadOnlyList<string>? recipients = null,
        TimeSpan? timeout = null) => new()
        {
            Enabled = true,
            ConfigurationMode = configurationMode,
            Host = "smtp.example.invalid",
            Port = port,
            SecurityMode = securityMode,
            AllowUnencryptedInternalRelay = allowUnencryptedInternalRelay,
            AuthenticationMode = authenticationMode,
            Username = username,
            Password = password,
            SenderAddress = senderAddress,
            Recipients = recipients ?? ["operator@example.invalid"],
            DestinationReference = "Operations",
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };
}
