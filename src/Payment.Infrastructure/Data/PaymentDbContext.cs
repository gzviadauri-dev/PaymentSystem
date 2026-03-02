using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Sagas;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
        // Default to no-tracking so UPDLOCK raw-SQL reads never collide with a
        // stale change-tracker snapshot from an earlier query in the same scope.
        // All writes are done via ExecuteSqlRawAsync — never SaveChangesAsync inside
        // a locked transaction.
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public DbSet<Domain.Entities.Payment> Payments => Set<Domain.Entities.Payment>();
    public DbSet<Balance> Balances => Set<Balance>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();
    public DbSet<MonthlyPaymentState> MonthlyPaymentStates => Set<MonthlyPaymentState>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Domain.Entities.Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.HasIndex(x => new { x.ExternalPaymentId, x.ProviderId })
             .IsUnique()
             .HasFilter("[ExternalPaymentId] IS NOT NULL");
        });

        mb.Entity<Balance>(e =>
        {
            e.HasKey(x => x.AccountId);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.ToTable(t => t.HasCheckConstraint("CHK_Balance_NonNegative", "[Amount] >= 0"));
        });

        mb.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(256);
            e.Property(x => x.LockedBy).HasMaxLength(200);
        });

        mb.Entity<DeadLetterMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(256);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.DeadLetteredAt);
            e.HasIndex(x => x.ReplayedOutboxMessageId);
        });

        mb.Entity<MonthlyPaymentState>(e =>
        {
            e.HasKey(x => x.CorrelationId);
            e.HasIndex(x => x.CurrentState);
            e.HasIndex(x => x.TimeoutAt);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            // Prevent duplicate active sagas for the same (LicenseId, Month).
            // Completed and Overdue terminal states are excluded so historical records are kept.
            e.HasIndex(x => new { x.LicenseId, x.Month })
             .IsUnique()
             .HasFilter("[CurrentState] NOT IN ('Completed', 'Overdue')");
        });

        mb.AddInboxStateEntity();
        mb.AddOutboxMessageEntity();
        mb.AddOutboxStateEntity();
    }
}
