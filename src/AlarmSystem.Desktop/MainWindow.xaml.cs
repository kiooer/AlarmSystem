using System.Windows;
using AlarmSystem.Desktop.ViewModels;

namespace AlarmSystem.Desktop;

public partial class MainWindow : Window
{
    private readonly LineNotificationTestViewModel viewModel;

    public MainWindow(LineNotificationTestViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (viewModel.CancelCommand.CanExecute(null))
        {
            viewModel.CancelCommand.Execute(null);
        }

        ChannelAccessTokenPasswordBox.Clear();
        viewModel.SetChannelAccessTokenPresent(false);
        base.OnClosed(e);
    }

    private void ChannelAccessTokenPasswordBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        using var securePassword = ChannelAccessTokenPasswordBox.SecurePassword;
        viewModel.SetChannelAccessTokenPresent(securePassword.Length > 0);
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        var channelAccessToken = ChannelAccessTokenPasswordBox.Password;
        try
        {
            await viewModel.SendAsync(channelAccessToken);
        }
        finally
        {
            channelAccessToken = string.Empty;
        }
    }
}
