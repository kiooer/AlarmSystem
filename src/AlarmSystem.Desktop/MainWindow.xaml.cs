using System.Windows;
using AlarmSystem.Desktop.ViewModels;

namespace AlarmSystem.Desktop;

public partial class MainWindow : Window
{
    private readonly LineNotificationTestViewModel lineViewModel;
    private readonly EmailNotificationTestViewModel emailViewModel;

    public MainWindow(
        LineNotificationTestViewModel lineViewModel,
        EmailNotificationTestViewModel emailViewModel)
    {
        InitializeComponent();
        this.lineViewModel = lineViewModel;
        this.emailViewModel = emailViewModel;
        DataContext = lineViewModel;
        EmailTab.DataContext = emailViewModel;
        Loaded += MainWindow_OnLoaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (lineViewModel.CancelCommand.CanExecute(null))
        {
            lineViewModel.CancelCommand.Execute(null);
        }

        if (emailViewModel.CancelCommand.CanExecute(null))
        {
            emailViewModel.CancelCommand.Execute(null);
        }

        ChannelAccessTokenPasswordBox.Clear();
        lineViewModel.SetChannelAccessTokenPresent(false);
        EmailPasswordBox.Clear();
        emailViewModel.SetPasswordPresent(false);
        base.OnClosed(e);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e) =>
        await emailViewModel.RefreshLicenseStatusAsync();

    private void ChannelAccessTokenPasswordBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        using var securePassword = ChannelAccessTokenPasswordBox.SecurePassword;
        lineViewModel.SetChannelAccessTokenPresent(securePassword.Length > 0);
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        var channelAccessToken = ChannelAccessTokenPasswordBox.Password;
        try
        {
            await lineViewModel.SendAsync(channelAccessToken);
        }
        finally
        {
            channelAccessToken = string.Empty;
        }
    }

    private void EmailPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        using var securePassword = EmailPasswordBox.SecurePassword;
        emailViewModel.SetPasswordPresent(securePassword.Length > 0);
    }

    private async void RefreshEmailLicenseButton_OnClick(
        object sender,
        RoutedEventArgs e) => await emailViewModel.RefreshLicenseStatusAsync();

    private async void TestEmailConnectionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var password = EmailPasswordBox.Password;
        try
        {
            await emailViewModel.TestConnectionAsync(password);
        }
        finally
        {
            password = string.Empty;
        }
    }

    private async void SendEmailButton_OnClick(object sender, RoutedEventArgs e)
    {
        var password = EmailPasswordBox.Password;
        try
        {
            await emailViewModel.SendAsync(password);
        }
        finally
        {
            password = string.Empty;
        }
    }
}
