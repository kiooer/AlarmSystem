using System.IO;
using System.Windows;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;
using AlarmSystem.Infrastructure.Licensing;
using AlarmSystem.Infrastructure.Notifications.Email;
using AlarmSystem.Infrastructure.Notifications.Line;
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
        services.AddSingleton(_ => new LineFlexMessageTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "line-alarm-card.json")));
        services.AddSingleton<ILineNotificationTestService, LineNotificationTestService>();
        services.AddSingleton<ILineSendConfirmationService, LineSendConfirmationService>();
        services.AddTransient<LineNotificationTestViewModel>();

        var dongleOptions = new DongleLicenseOptions();
        dongleOptions.Validate();
        services.AddSingleton(dongleOptions);
        services.AddSingleton<INotificationLicenseAuthorizer, UnavailableDongleLicenseAuthorizer>();
        services.AddSingleton<EmailMessageBuilder>();
        services.AddSingleton(provider => new EmailTemplateProvider(
            Path.Combine(AppContext.BaseDirectory, "templates", "email-alarm.html"),
            provider.GetRequiredService<EmailMessageBuilder>()));
        services.AddSingleton<IEmailSmtpClientFactory, MailKitEmailSmtpClientFactory>();
        services.AddSingleton<IEmailNotificationTestService, EmailNotificationTestService>();
        services.AddSingleton<IEmailSendConfirmationService, EmailSendConfirmationService>();
        services.AddTransient<EmailNotificationTestViewModel>();
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
