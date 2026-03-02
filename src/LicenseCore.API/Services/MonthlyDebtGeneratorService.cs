using System.Diagnostics.Metrics;
using LicenseCore.API.Data;
using LicenseCore.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Events;

namespace LicenseCore.API.Services;

public class MonthlyDebtGeneratorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockFactory _lockFactory;
    private readonly ILogger<MonthlyDebtGeneratorService> _logger;
    private readonly Counter<int> _dispatchFailureCounter;

    private static readonly Meter _meter = new("LicenseCore.API", "1.0");

    public MonthlyDebtGeneratorService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockFactory lockFactory,
        ILogger<MonthlyDebtGeneratorService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockFactory = lockFactory;
        _logger = logger;
        _dispatchFailureCounter = _meter.CreateCounter<int>(
            "monthly_debt_dispatch_failures",
            unit: "{failure}",
            description: "Number of per-license failures during the monthly debt dispatch run.");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, 1, 0, 1, 0, DateTimeKind.Utc)
                .AddMonths(1);
            var delay = nextRun - now;

            _logger.LogInformation(
                "MonthlyDebtGenerator: next run at {NextRun} (in {Delay})",
                nextRun, delay);

            await Task.Delay(delay, ct);
            await GenerateDebtsAsync(nextRun, ct);
        }
    }

    private async Task GenerateDebtsAsync(DateTime month, CancellationToken ct)
    {
        var thisMonth = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lockName = $"MonthlyDebtDispatch:{thisMonth:yyyy-MM}";

        // Redis advisory lock: only one pod runs the monthly dispatch per calendar month.
        // Lock key includes the month so locks from different months never block each other.
        // Renewal every 30s keeps the lock alive for runs that take several minutes.
        // If lock acquisition fails, another instance is already running — skip safely.
        await using var lockHandle = await _lockFactory.TryAcquireAsync(
            lockName,
            lockDuration: TimeSpan.FromMinutes(10),
            renewalInterval: TimeSpan.FromSeconds(30),
            ct);

        if (lockHandle == null)
        {
            _logger.LogInformation(
                "Monthly dispatch for {Month} already running on another instance — skipping.",
                thisMonth);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseCoreDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var licenses = await db.Licenses
            .Include(l => l.Vehicle)
            .Include(l => l.Drivers)
            .Where(l => l.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        _logger.LogInformation("Generating monthly debts for {Count} licenses", licenses.Count);

        var failedCount = 0;

        foreach (var license in licenses)
        {
            var amount = CalculateMonthlyAmount(license);
            if (amount == 0) continue;

            try
            {
                // Atomic INSERT WHERE NOT EXISTS — idempotent guard against message broker
                // redelivery scenarios where the Redis lock is not the relevant protection.
                var inserted = await db.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO MonthlyDebtDispatches (Id, LicenseId, Month, DispatchedAt)
                    SELECT {0}, {1}, {2}, {3}
                    WHERE NOT EXISTS (
                        SELECT 1 FROM MonthlyDebtDispatches
                        WHERE LicenseId = {1} AND Month = {2})",
                    Guid.NewGuid(), license.Id, thisMonth, DateTime.UtcNow, ct);

                if (inserted == 0)
                {
                    _logger.LogDebug(
                        "Debt for license {LicenseId} month {Month} already dispatched — skipping",
                        license.Id, thisMonth);
                    continue;
                }

                await publishEndpoint.Publish(new MonthlyDebtCreatedEvent(
                    PaymentId: Guid.NewGuid(),
                    LicenseId: license.Id,
                    Amount: amount,
                    Month: month), ct);

                _logger.LogInformation(
                    "Published MonthlyDebtCreatedEvent for license {LicenseId}, amount {Amount}",
                    license.Id, amount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                _dispatchFailureCounter.Add(1,
                    new KeyValuePair<string, object?>("licenseId", license.Id.ToString()),
                    new KeyValuePair<string, object?>("month", thisMonth.ToString("yyyy-MM")));

                _logger.LogCritical(ex,
                    "MonthlyDebt dispatch FAILED for LicenseId={LicenseId} Month={Month}. " +
                    "This license will NOT receive a debt notification this month. Manual intervention required.",
                    license.Id, thisMonth);
            }
        }

        if (failedCount > 0)
            _logger.LogCritical(
                "MonthlyDebt dispatch completed with {FailedCount}/{TotalCount} failures for Month={Month}.",
                failedCount, licenses.Count, thisMonth);
        else
            _logger.LogInformation(
                "MonthlyDebt dispatch completed successfully for {TotalCount} licenses Month={Month}.",
                licenses.Count, thisMonth);
    }

    private static decimal CalculateMonthlyAmount(Entities.License license)
        => (license.HasVehicle ? 50m : 0m) + (license.DriverCount * 20m);
}
