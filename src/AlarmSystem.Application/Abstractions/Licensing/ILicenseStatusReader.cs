namespace AlarmSystem.Application.Abstractions.Licensing;

public interface ILicenseStatusReader
{
    Task<LicenseStatus> ReadStatusAsync(CancellationToken cancellationToken);
}
