namespace AlarmSystem.Infrastructure.Notifications.Email;

public static class EmailSmtpPortSecurityPolicy
{
    public const int StartTlsPort = 587;

    public const int SslOnConnectPort = 465;

    public static IReadOnlyList<int> StandardPorts { get; } =
        Array.AsReadOnly([StartTlsPort, SslOnConnectPort]);

    public static bool TryGetStandardSecurityMode(
        int port,
        out EmailSecurityMode securityMode)
    {
        securityMode = port switch
        {
            StartTlsPort => EmailSecurityMode.StartTls,
            SslOnConnectPort => EmailSecurityMode.SslTls,
            _ => default,
        };

        return port is StartTlsPort or SslOnConnectPort;
    }

    public static EmailSecurityMode GetStandardSecurityMode(int port) =>
        TryGetStandardSecurityMode(port, out var securityMode)
            ? securityMode
            : throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "Standard SMTP mode only supports ports 587 and 465.");
}
