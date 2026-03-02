namespace LicenseCore.Application.Interfaces;

/// <summary>
/// Abstracts all write operations that consumers perform against LicenseCoreDb.
/// All methods use raw SQL WHERE guards — safe for at-least-once MassTransit delivery.
/// </summary>
public interface ILicenseRepository
{
    Task ActivateVehicleForLicenseAsync(Guid licenseId, CancellationToken ct);
    Task ActivateDriverAsync(Guid driverId, Guid licenseId, CancellationToken ct);
    Task ActivateLicenseAsync(Guid licenseId, CancellationToken ct);
    Task DeactivateLicenseAsync(Guid licenseId, CancellationToken ct);
    Task MarkMonthPaidAsync(Guid licenseId, DateTime? month, CancellationToken ct);
}
