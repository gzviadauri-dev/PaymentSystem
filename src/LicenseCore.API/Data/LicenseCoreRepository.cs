using LicenseCore.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LicenseCore.API.Data;

/// <summary>
/// Concrete repository backed by LicenseCoreDbContext.
/// All methods use raw SQL with idempotency guards so at-least-once delivery is safe.
/// </summary>
public class LicenseCoreRepository : ILicenseRepository
{
    private readonly LicenseCoreDbContext _db;
    private readonly ILogger<LicenseCoreRepository> _logger;

    public LicenseCoreRepository(LicenseCoreDbContext db, ILogger<LicenseCoreRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ActivateVehicleForLicenseAsync(Guid licenseId, CancellationToken ct)
    {
        var rows = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Vehicles SET IsActive = 1 WHERE LicenseId = {0} AND IsActive = 0",
            new object[] { licenseId }, ct);

        if (rows > 0)
            _logger.LogInformation("Vehicle activated for license {LicenseId}", licenseId);
        else
            _logger.LogDebug("ActivateVehicle for license {LicenseId}: already active or not found — no-op", licenseId);
    }

    public async Task ActivateDriverAsync(Guid driverId, Guid licenseId, CancellationToken ct)
    {
        var rows = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Drivers SET IsActive = 1 WHERE Id = {0} AND LicenseId = {1} AND IsActive = 0",
            new object[] { driverId, licenseId }, ct);

        if (rows > 0)
            _logger.LogInformation("Driver {DriverId} activated for license {LicenseId}", driverId, licenseId);
        else
            _logger.LogDebug("ActivateDriver {DriverId}: already active or not found — no-op", driverId);
    }

    public async Task ActivateLicenseAsync(Guid licenseId, CancellationToken ct)
    {
        var rows = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Licenses SET IsActive = 1 WHERE Id = {0} AND IsActive = 0",
            new object[] { licenseId }, ct);

        if (rows > 0)
            _logger.LogInformation("License {LicenseId} activated", licenseId);
        else
            _logger.LogDebug("ActivateLicense {LicenseId}: already active or not found — no-op", licenseId);
    }

    public async Task DeactivateLicenseAsync(Guid licenseId, CancellationToken ct)
    {
        var rows = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Licenses SET IsActive = 0 WHERE Id = {0} AND IsActive = 1",
            new object[] { licenseId }, ct);

        if (rows > 0)
            _logger.LogInformation("License {LicenseId} deactivated", licenseId);
        else
            _logger.LogDebug("DeactivateLicense {LicenseId}: already inactive or not found — no-op", licenseId);
    }

    public async Task MarkMonthPaidAsync(Guid licenseId, DateTime? month, CancellationToken ct)
    {
        if (month is null)
        {
            _logger.LogWarning("MarkMonthPaidAsync called with null month for license {LicenseId}", licenseId);
            return;
        }

        var normalised = new DateTime(month.Value.Year, month.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // INSERT WHERE NOT EXISTS is idempotent — duplicate events are safe no-ops.
        // Table name matches EF DbSet property: DbSet<LicenseMonthlyPayment> MonthlyPayments
        var inserted = await _db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO MonthlyPayments (Id, LicenseId, Month, PaidAt)
            SELECT {0}, {1}, {2}, {3}
            WHERE NOT EXISTS (
                SELECT 1 FROM MonthlyPayments
                WHERE LicenseId = {1} AND Month = {2})",
            new object[] { Guid.NewGuid(), licenseId, normalised, DateTime.UtcNow }, ct);

        if (inserted > 0)
            _logger.LogInformation(
                "Monthly payment recorded for license {LicenseId}, month {Month}",
                licenseId, normalised);
        else
            _logger.LogInformation(
                "Month {Month} already recorded for license {LicenseId} — idempotent skip",
                normalised, licenseId);
    }
}
