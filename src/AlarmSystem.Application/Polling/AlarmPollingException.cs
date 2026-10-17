namespace AlarmSystem.Application.Polling;

public class AlarmPollingException : Exception
{
    public AlarmPollingException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AlarmPollingException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class SourceIdentityCollisionException : AlarmPollingException
{
    public SourceIdentityCollisionException(Guid sourceMessageId)
        : base(
            "identity_collision",
            $"SourceMessageId '{sourceMessageId:D}' was reused with different source content; checkpoint advancement was refused.")
    {
    }
}
