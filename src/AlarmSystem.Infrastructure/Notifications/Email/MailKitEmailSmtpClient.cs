using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed class MailKitEmailSmtpClientFactory : IEmailSmtpClientFactory
{
    public IEmailSmtpClient Create() => new MailKitEmailSmtpClient();
}

internal sealed class MailKitEmailSmtpClient : IEmailSmtpClient
{
    private readonly SmtpClient client = new();

    public Task ConnectAsync(
        string host,
        int port,
        EmailSecurityMode securityMode,
        CancellationToken cancellationToken) => client.ConnectAsync(
            host,
            port,
            securityMode switch
            {
                EmailSecurityMode.None => SecureSocketOptions.None,
                EmailSecurityMode.StartTls => SecureSocketOptions.StartTls,
                EmailSecurityMode.SslTls => SecureSocketOptions.SslOnConnect,
                _ => throw new ArgumentOutOfRangeException(nameof(securityMode)),
            },
            cancellationToken);

    public Task AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken) =>
        client.AuthenticateAsync(username, password, cancellationToken);

    public Task<string> SendAsync(
        MimeMessage message,
        CancellationToken cancellationToken) => client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken) =>
        client.DisconnectAsync(quit, cancellationToken);

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
