using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Application.Notifications;
using AlarmSystem.Infrastructure.Licensing;
using AlarmSystem.Infrastructure.Notifications.Line;
using AlarmSystem.Infrastructure.Notifications.Teams;
using AlarmSystem.Worker;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
