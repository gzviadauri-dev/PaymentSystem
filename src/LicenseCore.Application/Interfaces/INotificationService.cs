namespace LicenseCore.Application.Interfaces;

public interface INotificationService
{
    Task SendAsync(Guid licenseId, string message, string channel, CancellationToken ct);
}
