using LicenseCore.API.Data;
using LicenseCore.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicenseCore.API.Endpoints;

public static class LicenseEndpoints
{
    public static void MapLicenseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/licenses").WithTags("Licenses").RequireAuthorization();

        group.MapGet("/", async (LicenseCoreDbContext db, CancellationToken ct) =>
        {
            var licenses = await db.Licenses
                .Include(l => l.Vehicle)
                .Include(l => l.Drivers)
                .Select(l => ToDto(l))
                .ToListAsync(ct);
            return Results.Ok(licenses);
        }).WithName("GetLicenses");

        group.MapGet("/{id:guid}", async (Guid id, LicenseCoreDbContext db, CancellationToken ct) =>
        {
            var license = await db.Licenses
                .Include(l => l.Vehicle)
                .Include(l => l.Drivers)
                .FirstOrDefaultAsync(l => l.Id == id, ct);
            return license is null ? Results.NotFound() : Results.Ok(ToDto(license));
        }).WithName("GetLicense");

        group.MapPost("/", async (CreateLicenseRequest req, LicenseCoreDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.OwnerName))
                return Results.BadRequest(new { error = "OwnerName is required." });

            var license = new License
            {
                Id = Guid.NewGuid(),
                OwnerName = req.OwnerName.Trim(),
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };
            db.Licenses.Add(license);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/licenses/{license.Id}", ToDto(license));
        }).WithName("CreateLicense");
    }

    private static LicenseDto ToDto(License l) => new(
        l.Id,
        l.OwnerName,
        l.IsActive,
        l.CreatedAt,
        l.Vehicle is null ? null : new VehicleSummary(l.Vehicle.Id, l.Vehicle.PlateNumber, l.Vehicle.IsActive),
        l.Drivers.Select(d => new DriverSummary(d.Id, d.FullName, d.IsActive)).ToList());
}

public record CreateLicenseRequest(string OwnerName);

public record LicenseDto(
    Guid Id,
    string OwnerName,
    bool IsActive,
    DateTime CreatedAt,
    VehicleSummary? Vehicle,
    List<DriverSummary> Drivers);

public record VehicleSummary(Guid Id, string PlateNumber, bool IsActive);
public record DriverSummary(Guid Id, string FullName, bool IsActive);
