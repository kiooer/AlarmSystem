using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public interface IEmailSmtpClient : IAsyncDisposable
{
    Task ConnectAsync(
        string host,
        int port,
        EmailSecurityMode securityMode,
        CancellationToken cancellationToken);

    Task AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken);

    Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken);

    Task DisconnectAsync(bool quit, CancellationToken cancellationToken);
}

public interface IEmailSmtpClientFactory
{
    IEmailSmtpClient Create();
}
