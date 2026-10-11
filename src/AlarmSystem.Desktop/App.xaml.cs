using System.Windows;
using AlarmSystem.Desktop.Services;
using AlarmSystem.Desktop.ViewModels;
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
        services.AddSingleton<LineAlarmTextRenderer>();
        services.AddSingleton<ILineNotificationTestService, LineNotificationTestService>();
        services.AddSingleton<ILineSendConfirmationService, LineSendConfirmationService>();
        services.AddTransient<LineNotificationTestViewModel>();
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
