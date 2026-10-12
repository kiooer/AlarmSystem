namespace AlarmSystem.Desktop.Services;

public interface IEmailSendConfirmationService
{
    bool Confirm(
        string destinationReference,
        string senderAddress,
        int recipientCount,
        Guid eventId,
        string tag);
}
