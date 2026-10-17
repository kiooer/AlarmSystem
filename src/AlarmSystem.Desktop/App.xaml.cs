using System.IO;
using System.Windows;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;
using AlarmSystem.Infrastructure.Notifications.Email;
using AlarmSystem.Infrastructure.Licensing.Demo;
using AlarmSystem.Infrastructure.Notifications.Line;
using AlarmSystem.Infrastructure.Notifications.Teams;
using Microsoft.Extensions.DependencyInjection;

namespace AlarmSystem.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddHttpClient(LineNotificationTestService.HttpClientName);
        services.AddSingleton(TimeProvider.System);

        services.AddHttpClient(TeamsNotificationTestService.HttpClientName);
        services.AddSingleton(_ => new TeamsCardTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "teams-alarm-card.json")));
        services.AddSingleton<ITeamsNotificationTestService, TeamsNotificationTestService>();
        services.AddSingleton<ITeamsSendConfirmationService, TeamsSendConfirmationService>();
        services.AddTransient<TeamsNotificationTestViewModel>();

        services.AddSingleton(_ => new LineFlexMessageTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "line-alarm-card.json")));
        services.AddSingleton<ILineNotificationTestService, LineNotificationTestService>();
        services.AddSingleton<ILineSendConfirmationService, LineSendConfirmationService>();
        services.AddTransient<LineNotificationTestViewModel>();

        services.AddSingleton<EmailMessageBuilder>();
        services.AddSingleton(provider => new EmailTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "email-alarm.html"),
            provider.GetRequiredService<EmailMessageBuilder>()));
        services.AddSingleton<IEmailSmtpClientFactory, MailKitEmailSmtpClientFactory>();
        services.AddSingleton<IEmailNotificationTestService, EmailNotificationTestService>();
        services.AddSingleton<IEmailSendConfirmationService, EmailSendConfirmationService>();
        services.AddSingleton<IEmailTestResultDialogService, EmailTestResultDialogService>();
        services.AddTransient<EmailNotificationTestViewModel>();

        var demoLicenseOptions = new DemoLicenseOptions();
        demoLicenseOptions.Validate();
        services.AddSingleton(demoLicenseOptions);
        var isDevelopment = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
        services.AddSingleton<IDemoLicenseTrustStore>(_ =>
            DevelopmentDemoLicenseTrustStore.Create(isDevelopment));
        services.AddSingleton<IMachineFingerprintProvider, MachineFingerprintProvider>();
        services.AddSingleton<DemoLicenseVerifier>();
        services.AddSingleton<ILicenseTestService, LicenseTestService>();
        services.AddSingleton<ILicenseFilePicker, LicenseFilePicker>();
        services.AddSingleton<ILicenseImportConfirmationService, LicenseImportConfirmationService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddTransient<LicenseTestViewModel>();
        services.AddTransient<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        var window = serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
