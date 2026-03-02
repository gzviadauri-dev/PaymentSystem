using System.Text.RegularExpressions;
using LicenseCore.API.Data;
using LicenseCore.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicenseCore.API.Endpoints;

public static class VehicleEndpoints
{
    // Georgian/international plate format: letters, digits, hyphens — max 20 chars
    private static readonly Regex PlateRegex = new(@"^[A-Z0-9\-]{1,20}$", RegexOptions.Compiled);

    public static void MapVehicleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/vehicles").WithTags("Vehicles").RequireAuthorization();

        group.MapPost("/", async (AddVehicleRequest req, LicenseCoreDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.PlateNumber))
                return Results.BadRequest(new { error = "PlateNumber is required." });

            var normalised = req.PlateNumber.Trim().ToUpperInvariant();

            if (!PlateRegex.IsMatch(normalised))
                return Results.BadRequest(new
                {
                    error = "PlateNumber must contain only letters, digits and hyphens (max 20 chars)."
                });

            var license = await db.Licenses.FindAsync([req.LicenseId], ct);
            if (license is null)
                return Results.NotFound(new { error = "License not found." });

            // Check unique constraint at the application layer for a better error message
            var duplicate = await db.Vehicles.AnyAsync(v => v.PlateNumber == normalised, ct);
            if (duplicate)
                return Results.Conflict(new { error = $"PlateNumber '{normalised}' is already registered." });

            var vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                LicenseId = req.LicenseId,
                PlateNumber = normalised,
                IsActive = false
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/vehicles/{vehicle.Id}", new
            {
                vehicle.Id,
                vehicle.LicenseId,
                vehicle.PlateNumber,
                vehicle.IsActive
            });
        }).WithName("AddVehicle");
    }
}

public record AddVehicleRequest(Guid LicenseId, string PlateNumber);
