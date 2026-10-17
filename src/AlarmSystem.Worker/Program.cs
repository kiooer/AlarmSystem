using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Application.Abstractions.Sources;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Application.Polling;
using AlarmSystem.Infrastructure.Licensing;
using AlarmSystem.Infrastructure.Notifications.Email;
using AlarmSystem.Infrastructure.Notifications.Line;
using AlarmSystem.Infrastructure.Notifications.Teams;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using AlarmSystem.Infrastructure.Sources.IFix;
using AlarmSystem.Worker;

var builder = Host.CreateApplicationBuilder(args);

var pollingSection = builder.Configuration.GetSection(AlarmPollingOptions.SectionName);
var pollingOptions = new AlarmPollingOptions
{
    Enabled = bool.TryParse(pollingSection["Enabled"], out var pollingEnabled) && pollingEnabled,
    IntervalSeconds = int.TryParse(pollingSection["IntervalSeconds"], out var intervalSeconds)
        ? intervalSeconds
        : 5,
    OverlapSeconds = int.TryParse(pollingSection["OverlapSeconds"], out var overlapSeconds)
        ? overlapSeconds
        : 60,
    MaxBatchSize = int.TryParse(pollingSection["MaxBatchSize"], out var maxBatchSize)
        ? maxBatchSize
        : 500,
    CommandTimeoutSeconds = int.TryParse(
        pollingSection["CommandTimeoutSeconds"],
        out var pollingCommandTimeoutSeconds)
            ? pollingCommandTimeoutSeconds
            : 10,
    InitialLookbackHours = int.TryParse(
        pollingSection["InitialLookbackHours"],
        out var initialLookbackHours)
            ? initialLookbackHours
            : 24,
    MaxFailureBackoffSeconds = int.TryParse(
        pollingSection["MaxFailureBackoffSeconds"],
        out var maxFailureBackoffSeconds)
            ? maxFailureBackoffSeconds
            : 60,
    SourceTimeZoneId = pollingSection["SourceTimeZoneId"] ?? "Taipei Standard Time",
    SourceConfigPath = pollingSection["SourceConfigPath"] ?? string.Empty,
    AlarmStoreConfigPath = pollingSection["AlarmStoreConfigPath"] ?? string.Empty,
};
pollingOptions.Validate();
builder.Services.AddSingleton(pollingOptions);
builder.Services.AddSingleton(TimeProvider.System);

if (pollingOptions.Enabled)
{
    var sourceConfigPath = Path.GetFullPath(
        pollingOptions.SourceConfigPath,
        builder.Environment.ContentRootPath);
    var alarmStoreConfigPath = Path.GetFullPath(
        pollingOptions.AlarmStoreConfigPath,
        builder.Environment.ContentRootPath);
    var sourceSqlOptions = await SqlServerConnectionOptionsLoader.LoadAsync(sourceConfigPath);
    var alarmStoreSqlOptions = await SqlServerConnectionOptionsLoader.LoadAsync(alarmStoreConfigPath);
    if (sourceSqlOptions.Role != SqlServerConnectionRole.Source)
    {
        throw new AlarmPollingConfigurationException(
            "AlarmPolling.SourceConfigPath must reference a SQL configuration with Role=Source.");
    }

    if (alarmStoreSqlOptions.Role != SqlServerConnectionRole.AlarmStore)
    {
        throw new AlarmPollingConfigurationException(
            "AlarmPolling.AlarmStoreConfigPath must reference a SQL configuration with Role=AlarmStore.");
    }

    if (!string.Equals(sourceSqlOptions.Database, "IFIX-ALARM", StringComparison.OrdinalIgnoreCase))
    {
        throw new AlarmPollingConfigurationException(
            "AlarmPolling.SourceConfigPath must target the approved IFIX-ALARM database.");
    }

    if (string.Equals(sourceSqlOptions.Server, alarmStoreSqlOptions.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(sourceSqlOptions.Database, alarmStoreSqlOptions.Database, StringComparison.OrdinalIgnoreCase))
    {
        throw new AlarmPollingConfigurationException(
            "The Source and AlarmStore SQL targets must be separate server/database roles.");
    }

    var sourceFactory = new SqlServerConnectionFactory(
        sourceSqlOptions,
        "AlarmSystem.Worker.Source");
    var alarmStoreFactory = new SqlServerConnectionFactory(
        alarmStoreSqlOptions,
        "AlarmSystem.Worker.AlarmStore");
    var mapper = new IFixAlarmMapper();
    builder.Services.AddSingleton<IAlarmSourceReader>(new SqlAlarmReader(
        sourceSqlOptions,
        sourceFactory,
        mapper,
        pollingOptions.CommandTimeoutSeconds));
    builder.Services.AddSingleton<IAlarmSourceReader>(new SqlOperatorReader(
        sourceSqlOptions,
        sourceFactory,
        mapper,
        pollingOptions.CommandTimeoutSeconds));
    builder.Services.AddSingleton<IAlarmEventIngestionRepository>(
        new SqlAlarmEventIngestionRepository(
            alarmStoreSqlOptions,
            alarmStoreFactory,
            pollingOptions.CommandTimeoutSeconds));
    builder.Services.AddSingleton<AlarmPollingService>();
}

var dongleSection = builder.Configuration.GetSection(DongleLicenseOptions.SectionName);
var featureMappingSection = dongleSection.GetSection("FeatureMapping");
var dongleOptions = new DongleLicenseOptions
{
    Provider = dongleSection["Provider"] ?? "Unconfigured",
    CheckTimeout = TimeSpan.FromSeconds(
        int.TryParse(dongleSection["CheckTimeoutSeconds"], out var licenseTimeoutSeconds)
            ? licenseTimeoutSeconds
            : 5),
    FeatureMapping = Enum.GetValues<NotificationFeature>().ToDictionary(
        feature => feature,
        feature => featureMappingSection[feature.ToString()] ?? string.Empty),
};

dongleOptions.Validate();
builder.Services.AddSingleton(dongleOptions);
builder.Services.AddSingleton<INotificationLicenseAuthorizer, UnavailableDongleLicenseAuthorizer>();

var teamsSection = builder.Configuration.GetSection(TeamsOptions.SectionName);
var endpointValue = teamsSection["Endpoint"];
var teamsOptions = new TeamsOptions
{
    Enabled = bool.TryParse(teamsSection["Enabled"], out var enabled) && enabled,
    Endpoint = Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ? endpoint : null,
    DestinationReference = teamsSection["DestinationReference"] ?? string.Empty,
    TemplatePath = teamsSection["TemplatePath"] ?? "templates/teams-alarm-card.json",
    Timeout = TimeSpan.FromSeconds(
        int.TryParse(teamsSection["TimeoutSeconds"], out var timeoutSeconds)
            ? timeoutSeconds
            : 10),
};

teamsOptions.Validate();
builder.Services.AddSingleton(teamsOptions);
builder.Services.AddSingleton(_ => new TeamsCardTemplateProvider(
    Path.GetFullPath(teamsOptions.TemplatePath, AppContext.BaseDirectory)));
builder.Services.AddHttpClient<TeamsWebhookNotificationChannel>(client =>
{
    client.Timeout = teamsOptions.Timeout;
});
builder.Services.AddTransient<INotificationChannel>(services =>
    new LicensedNotificationChannel(
        services.GetRequiredService<TeamsWebhookNotificationChannel>(),
        services.GetRequiredService<INotificationLicenseAuthorizer>(),
        NotificationFeature.Teams));

var lineSection = builder.Configuration.GetSection(LineOptions.SectionName);
var lineOptions = new LineOptions
{
    Enabled = bool.TryParse(lineSection["Enabled"], out var lineEnabled) && lineEnabled,
    GroupId = lineSection["GroupId"] ?? string.Empty,
    ChannelAccessToken = lineSection["ChannelAccessToken"] ?? string.Empty,
    TemplatePath = lineSection["TemplatePath"] ?? "templates/line-alarm-card.json",
    Timeout = TimeSpan.FromSeconds(
        int.TryParse(lineSection["TimeoutSeconds"], out var lineTimeoutSeconds)
            ? lineTimeoutSeconds
            : 10),
};

lineOptions.Validate();
builder.Services.AddSingleton(lineOptions);
builder.Services.AddSingleton(_ => new LineFlexMessageTemplateProvider(
    Path.GetFullPath(lineOptions.TemplatePath, AppContext.BaseDirectory)));
builder.Services.AddHttpClient<LineMessagingApiNotificationChannel>(client =>
{
    client.Timeout = lineOptions.Timeout;
});
builder.Services.AddTransient<INotificationChannel>(services =>
    new LicensedNotificationChannel(
        services.GetRequiredService<LineMessagingApiNotificationChannel>(),
        services.GetRequiredService<INotificationLicenseAuthorizer>(),
        NotificationFeature.Line));

var emailSection = builder.Configuration.GetSection(EmailOptions.SectionName);
var configurationModeValue = emailSection["ConfigurationMode"];
var securityModeValue = emailSection["SecurityMode"];
var authenticationModeValue = emailSection["AuthenticationMode"];
var emailConfigurationMode = configurationModeValue is null
    ? EmailSmtpConfigurationMode.Advanced
    : Enum.TryParse<EmailSmtpConfigurationMode>(
        configurationModeValue,
        ignoreCase: true,
        out var configurationMode)
            ? configurationMode
            : (EmailSmtpConfigurationMode)0;
var emailPort = int.TryParse(emailSection["Port"], out var configuredEmailPort)
    ? configuredEmailPort
    : EmailSmtpPortSecurityPolicy.StartTlsPort;
var configuredEmailSecurityMode = securityModeValue is null
    ? EmailSecurityMode.StartTls
    : Enum.TryParse<EmailSecurityMode>(
        securityModeValue,
        ignoreCase: true,
        out var securityMode)
            ? securityMode
            : (EmailSecurityMode)0;
var emailSecurityMode = emailConfigurationMode == EmailSmtpConfigurationMode.Standard &&
    EmailSmtpPortSecurityPolicy.TryGetStandardSecurityMode(
        emailPort,
        out var standardSecurityMode)
        ? standardSecurityMode
        : configuredEmailSecurityMode;
var emailOptions = new EmailOptions
{
    Enabled = bool.TryParse(emailSection["Enabled"], out var emailEnabled) && emailEnabled,
    ConfigurationMode = emailConfigurationMode,
    Host = emailSection["Host"] ?? string.Empty,
    Port = emailPort,
    SecurityMode = emailSecurityMode,
    AllowUnencryptedInternalRelay =
        bool.TryParse(emailSection["AllowUnencryptedInternalRelay"], out var allowRelay) &&
        allowRelay,
    AuthenticationMode = authenticationModeValue is null
        ? EmailAuthenticationMode.Password
        : Enum.TryParse<EmailAuthenticationMode>(
            authenticationModeValue,
            ignoreCase: true,
            out var authenticationMode)
                ? authenticationMode
                : (EmailAuthenticationMode)0,
    Username = emailSection["Username"] ?? string.Empty,
    Password = emailSection["Password"] ?? string.Empty,
    SenderAddress = emailSection["SenderAddress"] ?? string.Empty,
    SenderDisplayName = emailSection["SenderDisplayName"] ?? "AlarmSystem",
    Recipients = emailSection.GetSection("Recipients")
        .GetChildren()
        .Select(recipient => recipient.Value)
        .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
        .Cast<string>()
        .ToArray(),
    DestinationReference = emailSection["DestinationReference"] ?? string.Empty,
    Timeout = TimeSpan.FromSeconds(
        int.TryParse(emailSection["TimeoutSeconds"], out var emailTimeoutSeconds)
            ? emailTimeoutSeconds
            : 15),
    TemplatePath = emailSection["TemplatePath"] ?? "templates/email-alarm.html",
};

emailOptions.Validate();
builder.Services.AddSingleton(emailOptions);
builder.Services.AddSingleton<EmailMessageBuilder>();
builder.Services.AddSingleton(services => new EmailTemplateProvider(
    Path.GetFullPath(emailOptions.TemplatePath, AppContext.BaseDirectory),
    services.GetRequiredService<EmailMessageBuilder>()));
builder.Services.AddSingleton<IEmailSmtpClientFactory, MailKitEmailSmtpClientFactory>();
builder.Services.AddTransient<EmailSmtpNotificationChannel>();
builder.Services.AddTransient<INotificationChannel>(services =>
    new LicensedNotificationChannel(
        services.GetRequiredService<EmailSmtpNotificationChannel>(),
        services.GetRequiredService<INotificationLicenseAuthorizer>(),
        NotificationFeature.Email));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
