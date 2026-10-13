using System.Diagnostics;
using System.ComponentModel;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlarmSystem.Infrastructure.Notifications.Email;
using MailKit.Net.Smtp;
using MailKit.Security;

const string defaultHost = "smtp.gmail.com";
const int defaultPort = 465;
const int defaultTimeoutSeconds = 60;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine(
        "Usage: dotnet run --project tools/AlarmSystem.SmtpDiagnostic -- " +
        "[host] [port] [SslOnConnect|StartTls] [timeoutSeconds]");
    return 0;
}

var host = args.Length > 0 ? args[0] : defaultHost;
if (string.IsNullOrWhiteSpace(host))
{
    Console.Error.WriteLine("Host must not be empty.");
    return 2;
}

if (!TryReadInt(args, 1, defaultPort, 1, 65535, out var port) ||
    !TryReadInt(args, 3, defaultTimeoutSeconds, 1, 120, out var timeoutSeconds))
{
    Console.Error.WriteLine("Port must be 1..65535 and timeoutSeconds must be 1..120.");
    return 2;
}

var security = SecureSocketOptions.SslOnConnect;
if (args.Length > 2 &&
    (!Enum.TryParse(args[2], ignoreCase: true, out security) ||
     security is not (SecureSocketOptions.SslOnConnect or SecureSocketOptions.StartTls)))
{
    Console.Error.WriteLine("Security must be SslOnConnect or StartTls.");
    return 2;
}

var securityMode = security == SecureSocketOptions.StartTls
    ? EmailSecurityMode.StartTls
    : EmailSecurityMode.SslTls;

var correlationId = Guid.NewGuid();
var started = Stopwatch.GetTimestamp();
var connectSucceeded = false;
var isSecure = false;
string? ipv4Address = null;
SafeExceptionDiagnostic? failure = null;
SafeExceptionDiagnostic? disconnectWarning = null;

using var callerCancellation = new CancellationTokenSource();
Console.CancelKeyPress += HandleCancel;
using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation.Token);
timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
await using var client = new MailKitEmailSmtpClientFactory().Create();

try
{
    await client.ConnectAsync(host, port, securityMode, timeout.Token);
    connectSucceeded = client.IsConnected;
    isSecure = client.IsSecure;
    ipv4Address = client.RemoteAddress;
}
catch (Exception exception)
{
    ipv4Address = client.RemoteAddress;
    failure = DescribeException(
        exception,
        callerCancellation.IsCancellationRequested,
        timeout.IsCancellationRequested && !callerCancellation.IsCancellationRequested);
}
finally
{
    if (client.IsConnected)
    {
        using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.DisconnectAsync(quit: true, disconnectTimeout.Token);
        }
        catch (Exception exception)
        {
            disconnectWarning = DescribeException(
                exception,
                cancellationRequested: false,
                timeoutTriggered: disconnectTimeout.IsCancellationRequested);
        }
    }

    Console.CancelKeyPress -= HandleCancel;
}

var output = new
{
    CorrelationId = correlationId,
    Stage = "Connect",
    Success = connectSucceeded,
    IsSecure = isSecure,
    Host = host,
    Ipv4Address = ipv4Address,
    Port = port,
    SecureSocketOptions = security.ToString(),
    TimeoutSeconds = timeoutSeconds,
    DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
    Runtime = RuntimeInformation.FrameworkDescription,
    Environment.Version,
    ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    MailKitVersion = typeof(MailKit.Net.Smtp.SmtpClient).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    Failure = failure,
    DisconnectWarning = disconnectWarning,
};

Console.WriteLine(JsonSerializer.Serialize(
    output,
    new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    }));

return connectSucceeded ? 0 : 1;

void HandleCancel(object? sender, ConsoleCancelEventArgs eventArgs)
{
    eventArgs.Cancel = true;
    callerCancellation.Cancel();
}

static bool TryReadInt(
    string[] values,
    int index,
    int defaultValue,
    int minimum,
    int maximum,
    out int parsed)
{
    if (values.Length <= index)
    {
        parsed = defaultValue;
        return true;
    }

    return int.TryParse(values[index], out parsed) && parsed >= minimum && parsed <= maximum;
}

static SafeExceptionDiagnostic DescribeException(
    Exception exception,
    bool cancellationRequested,
    bool timeoutTriggered)
{
    var socket = FindException<SocketException>(exception);
    var win32 = FindException<Win32Exception>(exception);
    var command = FindException<SmtpCommandException>(exception);
    var systemAuthentication =
        FindException<System.Security.Authentication.AuthenticationException>(exception);
    var mailKitAuthentication =
        FindException<MailKit.Security.AuthenticationException>(exception);
    var deepest = FindDeepestInnerException(exception);
    var errorCode = cancellationRequested
        ? "email_operation_cancelled"
        : timeoutTriggered
            ? "email_timeout"
            : EmailSmtpErrorClassifier.Classify(exception).ErrorCode;
    return new SafeExceptionDiagnostic(
        errorCode,
        GetExceptionType(exception),
        deepest is null ? null : GetExceptionType(deepest),
        socket?.SocketErrorCode,
        socket?.NativeErrorCode ?? win32?.NativeErrorCode,
        command is null ? null : (int)command.StatusCode,
        systemAuthentication is not null
            ? GetExceptionType(systemAuthentication)
            : mailKitAuthentication is null
                ? null
                : GetExceptionType(mailKitAuthentication),
        cancellationRequested,
        timeoutTriggered);
}

static TException? FindException<TException>(Exception exception)
    where TException : Exception
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is TException match)
        {
            return match;
        }
    }

    return null;
}

static Exception? FindDeepestInnerException(Exception exception)
{
    if (exception.InnerException is null)
    {
        return null;
    }

    var current = exception.InnerException;
    while (current.InnerException is not null)
    {
        current = current.InnerException;
    }

    return current;
}

static string GetExceptionType(Exception exception) =>
    exception.GetType().FullName ?? exception.GetType().Name;

internal sealed record SafeExceptionDiagnostic(
    string? ErrorCode,
    string ExceptionType,
    string? InnerExceptionType,
    SocketError? SocketErrorCode,
    int? NativeErrorCode,
    int? SmtpStatusCode,
    string? AuthenticationExceptionType,
    bool CancellationRequested,
    bool TimeoutTriggered);
