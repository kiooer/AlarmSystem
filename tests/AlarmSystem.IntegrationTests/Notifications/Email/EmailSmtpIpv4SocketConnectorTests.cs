using System.Net;
using System.Net.Sockets;
using AlarmSystem.Infrastructure.Notifications.Email;
using MailKit.Net.Smtp;

namespace AlarmSystem.IntegrationTests.Notifications.Email;

public sealed class EmailSmtpIpv4SocketConnectorTests
{
    [Fact]
    public async Task ConnectAsync_FiltersOutIpv6AndCreatesIpv4Socket()
    {
        var endpoints = new List<IPEndPoint>();
        var connector = CreateConnector(
            [IPAddress.IPv6Loopback, IPAddress.Parse("192.0.2.10")],
            (socket, endpoint, _) =>
            {
                Assert.Equal(AddressFamily.InterNetwork, socket.AddressFamily);
                endpoints.Add(Assert.IsType<IPEndPoint>(endpoint));
                return ValueTask.CompletedTask;
            });

        using var connection = await connector.ConnectAsync(
            "smtp.example.invalid",
            465,
            CancellationToken.None);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), endpoint.Address);
        Assert.Equal(465, endpoint.Port);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), connection.Address);
        Assert.Equal(AddressFamily.InterNetwork, connection.Socket.AddressFamily);
    }

    [Fact]
    public async Task ConnectAsync_WhenFirstIpv4Fails_TriesNextAndDisposesFailedSocket()
    {
        var first = IPAddress.Parse("192.0.2.11");
        var second = IPAddress.Parse("192.0.2.12");
        var sockets = new List<Socket>();
        var attempts = new List<IPAddress>();
        var connector = new EmailSmtpIpv4SocketConnector(
            new StubDnsResolver([first, second]),
            () =>
            {
                var socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                sockets.Add(socket);
                return socket;
            },
            (_, endpoint, _) =>
            {
                var address = Assert.IsType<IPEndPoint>(endpoint).Address;
                attempts.Add(address);
                return address.Equals(first)
                    ? new ValueTask(Task.FromException(
                        new SocketException((int)SocketError.ConnectionRefused)))
                    : ValueTask.CompletedTask;
            });

        using var connection = await connector.ConnectAsync(
            "smtp.example.invalid",
            587,
            CancellationToken.None);

        Assert.Equal([first, second], attempts);
        Assert.True(sockets[0].SafeHandle.IsClosed);
        Assert.False(sockets[1].SafeHandle.IsClosed);
        Assert.Equal(second, connection.Address);
    }

    [Fact]
    public async Task ConnectAsync_WhenDnsHasNoIpv4_ReturnsExplicitErrorWithoutCreatingSocket()
    {
        var socketCreateCount = 0;
        var connector = new EmailSmtpIpv4SocketConnector(
            new StubDnsResolver([IPAddress.IPv6Loopback]),
            () =>
            {
                socketCreateCount++;
                return new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
            },
            static (_, _, _) => ValueTask.CompletedTask);

        var exception = await Assert.ThrowsAsync<EmailSmtpIpv4AddressNotFoundException>(
            () => connector.ConnectAsync(
                "smtp.example.invalid",
                465,
                CancellationToken.None));

        Assert.Equal("smtp.example.invalid", exception.Host);
        Assert.Equal(0, socketCreateCount);
    }

    [Fact]
    public async Task ConnectAsync_WhenCancelled_DoesNotAttemptTcpConnection()
    {
        var connectCount = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var connector = CreateConnector(
            [IPAddress.Parse("192.0.2.13")],
            (_, _, _) =>
            {
                connectCount++;
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.ConnectAsync(
                "smtp.example.invalid",
                465,
                cancellation.Token));

        Assert.Equal(0, connectCount);
    }

    [Fact]
    public async Task MailKitClient_DoesNotOverrideCertificateValidation()
    {
        using var mailKitClient = new SmtpClient();
        var connector = CreateConnector(
            [IPAddress.Parse("192.0.2.14")],
            static (_, _, _) => ValueTask.CompletedTask);
        await using var client = new MailKitEmailSmtpClient(mailKitClient, connector);

        Assert.Null(mailKitClient.ServerCertificateValidationCallback);
    }

    private static EmailSmtpIpv4SocketConnector CreateConnector(
        IPAddress[] addresses,
        Func<Socket, EndPoint, CancellationToken, ValueTask> connect) => new(
            new StubDnsResolver(addresses),
            static () => new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp),
            connect);

    private sealed class StubDnsResolver(IPAddress[] addresses) : IEmailSmtpDnsResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(
            string host,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(addresses);
        }
    }
}
