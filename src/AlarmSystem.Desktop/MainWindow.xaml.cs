using System.Windows;
using AlarmSystem.Desktop.ViewModels;

namespace AlarmSystem.Desktop;

public partial class MainWindow : Window
{
    private readonly TeamsNotificationTestViewModel teamsViewModel;
    private readonly LineNotificationTestViewModel lineViewModel;
    private readonly EmailNotificationTestViewModel emailViewModel;
    private readonly LicenseTestViewModel licenseViewModel;

    public MainWindow(
        TeamsNotificationTestViewModel teamsViewModel,
        LineNotificationTestViewModel lineViewModel,
        EmailNotificationTestViewModel emailViewModel,
        LicenseTestViewModel licenseViewModel)
    {
        InitializeComponent();
        this.teamsViewModel = teamsViewModel;
        this.lineViewModel = lineViewModel;
        this.emailViewModel = emailViewModel;
        this.licenseViewModel = licenseViewModel;
        DataContext = lineViewModel;
        TeamsTab.DataContext = teamsViewModel;
        EmailTab.DataContext = emailViewModel;
        LicenseTab.DataContext = licenseViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await licenseViewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (teamsViewModel.CancelCommand.CanExecute(null))
        {
            teamsViewModel.CancelCommand.Execute(null);
        }

        if (lineViewModel.CancelCommand.CanExecute(null))
        {
            lineViewModel.CancelCommand.Execute(null);
        }

        if (emailViewModel.CancelCommand.CanExecute(null))
        {
            emailViewModel.CancelCommand.Execute(null);
        }

        TeamsWebhookUrlPasswordBox.Clear();
        teamsViewModel.SetWebhookUrlPresent(false);
        ChannelAccessTokenPasswordBox.Clear();
        lineViewModel.SetChannelAccessTokenPresent(false);
        EmailPasswordBox.Clear();
        base.OnClosed(e);
    }

    private void TeamsWebhookUrlPasswordBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        using var securePassword = TeamsWebhookUrlPasswordBox.SecurePassword;
        teamsViewModel.SetWebhookUrlPresent(securePassword.Length > 0);
    }

    private async void SendTeamsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var webhookUrl = TeamsWebhookUrlPasswordBox.Password;
        try
        {
            await teamsViewModel.SendAsync(webhookUrl);
        }
        finally
        {
            webhookUrl = string.Empty;
        }
    }

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
