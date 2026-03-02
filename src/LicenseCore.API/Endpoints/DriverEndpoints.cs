using LicenseCore.API.Data;
using LicenseCore.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicenseCore.API.Endpoints;

public static class DriverEndpoints
{
    public static void MapDriverEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/drivers").WithTags("Drivers").RequireAuthorization();

        group.MapPost("/", async (AddDriverRequest req, LicenseCoreDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.FullName))
                return Results.BadRequest(new { error = "FullName is required." });

            var license = await db.Licenses.FindAsync([req.LicenseId], ct);
            if (license is null) return Results.NotFound(new { error = "License not found." });

            var driver = new Driver
            {
                Id = Guid.NewGuid(),
                LicenseId = req.LicenseId,
                FullName = req.FullName.Trim(),
                IsActive = false
            };
            db.Drivers.Add(driver);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/drivers/{driver.Id}", new
            {
                driver.Id,
                driver.LicenseId,
                driver.FullName,
                driver.IsActive
            });
        }).WithName("AddDriver");

        group.MapGet("/{licenseId:guid}", async (Guid licenseId, LicenseCoreDbContext db, CancellationToken ct) =>
        {
            var drivers = await db.Drivers
                .Where(d => d.LicenseId == licenseId)
                .Select(d => new { d.Id, d.FullName, d.IsActive })
                .ToListAsync(ct);
            return Results.Ok(drivers);
        }).WithName("GetDrivers");
    }
}

public record AddDriverRequest(Guid LicenseId, string FullName);
