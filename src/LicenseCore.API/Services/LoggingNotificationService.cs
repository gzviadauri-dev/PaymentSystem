using LicenseCore.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace LicenseCore.API.Services;
// Concrete implementation lives in the host project; the interface is in LicenseCore.Application.

public class LoggingNotificationService : INotificationService
{
    private readonly ILogger<LoggingNotificationService> _logger;

    public LoggingNotificationService(ILogger<LoggingNotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(Guid licenseId, string message, string channel, CancellationToken ct)
    {
        _logger.LogInformation(
            "Notification [{Channel}] → License {LicenseId}: {Message}",
            channel, licenseId, message);

        // TODO: inject IEmailService / ISmsService / IPushService and route by channel.
        return Task.CompletedTask;
    }
}
