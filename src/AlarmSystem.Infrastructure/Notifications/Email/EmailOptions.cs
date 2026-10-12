using MimeKit;

namespace AlarmSystem.Infrastructure.Notifications.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Notifications:Email";

    public bool Enabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public EmailSecurityMode SecurityMode { get; init; } = EmailSecurityMode.StartTls;

    public bool AllowUnencryptedInternalRelay { get; init; }

    public EmailAuthenticationMode AuthenticationMode { get; init; } =
        EmailAuthenticationMode.Password;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string SenderAddress { get; init; } = string.Empty;

    public string SenderDisplayName { get; init; } = "AlarmSystem";

    public IReadOnlyList<string> Recipients { get; init; } = [];

    public string DestinationReference { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public string TemplatePath { get; init; } = "templates/email-alarm.html";

    public void Validate()
    {
        if (!Enum.IsDefined(SecurityMode))
        {
            throw new InvalidOperationException($"{SectionName}:SecurityMode is not supported.");
        }

        if (!Enum.IsDefined(AuthenticationMode))
        {
            throw new InvalidOperationException(
                $"{SectionName}:AuthenticationMode is not supported.");
        }

        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Timeout must be greater than zero and no more than two minutes.");
        }

        if (string.IsNullOrWhiteSpace(TemplatePath))
        {
            throw new InvalidOperationException($"{SectionName}:TemplatePath is required.");
        }

        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Host) || Host.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Host is required and cannot contain whitespace when Email is enabled.");
        }

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"{SectionName}:Port must be between 1 and 65535 when Email is enabled.");
        }

        if (SecurityMode == EmailSecurityMode.None)
        {
            if (!AllowUnencryptedInternalRelay)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:AllowUnencryptedInternalRelay must be explicitly enabled for an unencrypted relay.");
            }

            if (AuthenticationMode != EmailAuthenticationMode.None)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:Password authentication is not allowed over an unencrypted relay.");
            }
        }

        if (AuthenticationMode == EmailAuthenticationMode.Password &&
            (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Username and Password are required for password authentication.");
        }

        if (!TryParseMailbox(SenderAddress))
        {
            throw new InvalidOperationException(
                $"{SectionName}:SenderAddress must be a valid email address.");
        }

        if (Recipients.Count == 0 || Recipients.Any(recipient => !TryParseMailbox(recipient)))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Recipients must contain only valid email addresses.");
        }

        if (string.IsNullOrWhiteSpace(DestinationReference))
        {
            throw new InvalidOperationException(
                $"{SectionName}:DestinationReference is required when Email is enabled.");
        }
    }

    private static bool TryParseMailbox(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!MailboxAddress.TryParse(value, out var mailbox))
        {
            return false;
        }

        var separator = mailbox.Address.LastIndexOf('@');
        return separator > 0 && separator < mailbox.Address.Length - 1;
    }
}
