using LicenseCore.API.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace LicenseCore.API.Data;

public class LicenseCoreDbContext : DbContext
{
    public LicenseCoreDbContext(DbContextOptions<LicenseCoreDbContext> options) : base(options) { }

    public DbSet<License> Licenses => Set<License>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<LicenseMonthlyPayment> MonthlyPayments => Set<LicenseMonthlyPayment>();
    public DbSet<MonthlyDebtDispatch> MonthlyDebtDispatches => Set<MonthlyDebtDispatch>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<License>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Vehicle).WithOne(v => v.License).HasForeignKey<Vehicle>(v => v.LicenseId);
            e.HasMany(x => x.Drivers).WithOne(d => d.License).HasForeignKey(d => d.LicenseId);
            e.HasMany<LicenseMonthlyPayment>().WithOne(m => m.License).HasForeignKey(m => m.LicenseId);
            e.HasMany<MonthlyDebtDispatch>().WithOne(d => d.License).HasForeignKey(d => d.LicenseId);
        });

        mb.Entity<Vehicle>(e =>
        {
            e.HasKey(x => x.Id);
            // Plate numbers are normalised to UPPER in the endpoint before persistence.
            // The unique index prevents the same plate being registered on two licenses.
            e.HasIndex(x => x.PlateNumber).IsUnique();
            e.Property(x => x.PlateNumber).HasMaxLength(20);
        });

        mb.Entity<Driver>(e => e.HasKey(x => x.Id));

        mb.Entity<LicenseMonthlyPayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LicenseId, x.Month }).IsUnique();
        });

        mb.Entity<MonthlyDebtDispatch>(e =>
        {
            e.HasKey(x => x.Id);
            // Prevents re-dispatching a debt for the same license+month on service restart
            e.HasIndex(x => new { x.LicenseId, x.Month }).IsUnique();
        });

        mb.AddInboxStateEntity();
        mb.AddOutboxMessageEntity();
        mb.AddOutboxStateEntity();
    }
}
