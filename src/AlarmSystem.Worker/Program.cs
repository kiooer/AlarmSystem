using AlarmSystem.Application.Abstractions.Notifications;
using AlarmSystem.Infrastructure.Notifications.Line;
using AlarmSystem.Infrastructure.Notifications.Teams;
using AlarmSystem.Worker;

var builder = Host.CreateApplicationBuilder(args);

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
    services.GetRequiredService<TeamsWebhookNotificationChannel>());

var lineSection = builder.Configuration.GetSection(LineOptions.SectionName);
var lineOptions = new LineOptions
{
    Enabled = bool.TryParse(lineSection["Enabled"], out var lineEnabled) && lineEnabled,
    GroupId = lineSection["GroupId"] ?? string.Empty,
    ChannelAccessToken = lineSection["ChannelAccessToken"] ?? string.Empty,
    Timeout = TimeSpan.FromSeconds(
        int.TryParse(lineSection["TimeoutSeconds"], out var lineTimeoutSeconds)
            ? lineTimeoutSeconds
            : 10),
};

lineOptions.Validate();
builder.Services.AddSingleton(lineOptions);
builder.Services.AddSingleton<LineAlarmTextRenderer>();
builder.Services.AddHttpClient<LineMessagingApiNotificationChannel>(client =>
{
    client.Timeout = lineOptions.Timeout;
});
builder.Services.AddTransient<INotificationChannel>(services =>
    services.GetRequiredService<LineMessagingApiNotificationChannel>());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
