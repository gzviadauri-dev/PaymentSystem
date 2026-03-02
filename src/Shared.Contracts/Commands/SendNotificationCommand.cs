namespace Shared.Contracts.Commands;

public record SendNotificationCommand(
    Guid LicenseId,
    string Message);
