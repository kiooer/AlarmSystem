using System.Net;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed class MailKitEmailSmtpClientFactory : IEmailSmtpClientFactory
{
    public IEmailSmtpClient Create() => new MailKitEmailSmtpClient();
}

public sealed class EmailSmtpIpv4AddressNotFoundException : Exception
{
    public EmailSmtpIpv4AddressNotFoundException(string host)
        : base("The SMTP host did not resolve to an IPv4 address.")
    {
        Host = host;
    }

    public string Host { get; }
}

public static class EmailSmtpSecurityModeMapper
{
    public static SecureSocketOptions ToSecureSocketOptions(
        EmailSecurityMode securityMode) => securityMode switch
        {
            EmailSecurityMode.None => SecureSocketOptions.None,
            EmailSecurityMode.StartTls => SecureSocketOptions.StartTls,
            EmailSecurityMode.SslTls => SecureSocketOptions.SslOnConnect,
            _ => throw new ArgumentOutOfRangeException(nameof(securityMode)),
        };
}

internal sealed class MailKitEmailSmtpClient : IEmailSmtpClient
{
    private readonly SmtpClient client;
    private readonly EmailSmtpIpv4SocketConnector socketConnector;

    public MailKitEmailSmtpClient()
        : this(new SmtpClient(), new EmailSmtpIpv4SocketConnector())
    {
    }

    internal MailKitEmailSmtpClient(
        SmtpClient client,
        EmailSmtpIpv4SocketConnector socketConnector)
    {
        this.client = client;
        this.socketConnector = socketConnector;
    }

    public bool IsConnected => client.IsConnected;

    public bool IsSecure => client.IsSecure;

    public string? RemoteAddress { get; private set; }

    public async Task ConnectAsync(
        string host,
        int port,
        EmailSecurityMode securityMode,
        CancellationToken cancellationToken)
    {
        RemoteAddress = null;

        using var connection = await socketConnector
            .ConnectAsync(host, port, cancellationToken)
            .ConfigureAwait(false);

        RemoteAddress = connection.Address.ToString();

        await client.ConnectAsync(
                connection.Socket,
                host,
                port,
                EmailSmtpSecurityModeMapper.ToSecureSocketOptions(securityMode),
                cancellationToken)
            .ConfigureAwait(false);

        connection.TransferOwnership();
    }

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

internal interface IEmailSmtpDnsResolver
{
    Task<IPAddress[]> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken);
}

internal sealed class SystemEmailSmtpDnsResolver : IEmailSmtpDnsResolver
{
    public Task<IPAddress[]> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

internal sealed class EmailSmtpIpv4SocketConnector
{
    private readonly IEmailSmtpDnsResolver dnsResolver;
    private readonly Func<Socket> socketFactory;
    private readonly Func<Socket, EndPoint, CancellationToken, ValueTask> connectSocketAsync;

    public EmailSmtpIpv4SocketConnector()
        : this(
            new SystemEmailSmtpDnsResolver(),
            static () => new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp),
            static (socket, endPoint, cancellationToken) =>
                socket.ConnectAsync(endPoint, cancellationToken))
    {
    }

    internal EmailSmtpIpv4SocketConnector(
        IEmailSmtpDnsResolver dnsResolver,
        Func<Socket> socketFactory,
        Func<Socket, EndPoint, CancellationToken, ValueTask> connectSocketAsync)
    {
        this.dnsResolver = dnsResolver;
        this.socketFactory = socketFactory;
        this.connectSocketAsync = connectSocketAsync;
    }

    public async Task<EmailSmtpIpv4SocketConnection> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        var addresses = await dnsResolver
            .GetHostAddressesAsync(host, cancellationToken)
            .ConfigureAwait(false);
        var ipv4Addresses = addresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .ToArray();

        if (ipv4Addresses.Length == 0)
        {
            throw new EmailSmtpIpv4AddressNotFoundException(host);
        }

        SocketException? lastSocketException = null;

        foreach (var address in ipv4Addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var socket = socketFactory();
            try
            {
                await connectSocketAsync(
                        socket,
                        new IPEndPoint(address, port),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new EmailSmtpIpv4SocketConnection(socket, address);
            }
            catch (SocketException exception)
            {
                lastSocketException = exception;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw (Exception?)lastSocketException ??
            new InvalidOperationException("The IPv4 SMTP connection attempts did not complete.");
    }
}

internal sealed class EmailSmtpIpv4SocketConnection : IDisposable
{
    private bool ownsSocket = true;

    public EmailSmtpIpv4SocketConnection(Socket socket, IPAddress address)
    {
        Socket = socket;
        Address = address;
    }

    public Socket Socket { get; }

    public IPAddress Address { get; }

    public void TransferOwnership() => ownsSocket = false;

    public void Dispose()
    {
        if (ownsSocket)
        {
            Socket.Dispose();
        }
    }
}
