using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Interfaces;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using Shared.Contracts.Events;

namespace Payment.Infrastructure.Repositories;

public class BalanceRepository : IBalanceRepository
{
    private readonly PaymentDbContext _db;

    public BalanceRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<Balance?> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
        => await _db.Balances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountId == accountId, ct);

    public async Task<Balance> GetOrCreateAsync(Guid accountId, CancellationToken ct = default)
    {
        // INSERT ... WHERE NOT EXISTS is atomic — safe under concurrent calls
        await _db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO Balances (AccountId, Amount, UpdatedAt)
            SELECT {0}, 0, {1}
            WHERE NOT EXISTS (SELECT 1 FROM Balances WHERE AccountId = {0})",
            accountId, DateTime.UtcNow, ct);

        return (await GetByAccountIdAsync(accountId, ct))!;
    }

    /// <inheritdoc/>
    public async Task<TryDebitAndCompletePaymentResult> TryDebitAndCompletePaymentAsync(
        Guid accountId, Guid paymentId, decimal amount, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // Lock and read Balance with UPDLOCK to prevent concurrent debits
            var balance = await _db.Balances
                .FromSqlRaw("SELECT * FROM Balances WITH (UPDLOCK, ROWLOCK) WHERE AccountId = {0}", accountId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (balance is null || balance.Amount < amount)
            {
                await tx.RollbackAsync(ct);
                return new TryDebitAndCompletePaymentResult(TryDebitResult.InsufficientBalance, balance?.Amount ?? 0m);
            }

            // Lock and read Payment — guard against duplicate processing
            var payment = await _db.Payments
                .FromSqlRaw("SELECT * FROM Payments WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", paymentId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (payment is null || payment.Status != PaymentStatus.Pending)
            {
                await tx.RollbackAsync(ct);
                return new TryDebitAndCompletePaymentResult(TryDebitResult.AlreadyProcessed, 0m);
            }

            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Balances SET Amount = Amount - {0}, UpdatedAt = {1} WHERE AccountId = {2}",
                amount, DateTime.UtcNow, accountId, ct);

            // Secondary WHERE guard: CAS on Status = 'Pending' to catch races after the lock read
            var paymentRows = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Payments SET Status = 'Paid', PaidAt = {0} WHERE Id = {1} AND Status = 'Pending'",
                DateTime.UtcNow, paymentId, ct);

            if (paymentRows == 0)
            {
                await tx.RollbackAsync(ct);
                return new TryDebitAndCompletePaymentResult(TryDebitResult.AlreadyProcessed, 0m);
            }

            // Write BalanceDebitedEvent to the custom outbox inside the same transaction.
            // OutboxProcessor will publish it to RabbitMQ; the saga receives it and finalises.
            var outboxJson = JsonSerializer.Serialize(new BalanceDebitedEvent(paymentId, amount));
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO OutboxMessages (Id, Type, Payload, CreatedAt, RetryCount) VALUES ({0},{1},{2},{3},0)",
                Guid.NewGuid(), nameof(BalanceDebitedEvent), outboxJson, DateTime.UtcNow, ct);

            await tx.CommitAsync(ct);
            return new TryDebitAndCompletePaymentResult(TryDebitResult.Success, 0m);
        });
    }

    /// <inheritdoc/>
    public async Task CreditAsync(Guid accountId, decimal amount, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Try UPDATE first; if the row doesn't exist yet, INSERT with NOT EXISTS guard.
        // Catch unique-constraint races (SQL 2627/2601) and retry with the UPDATE.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var rows = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Balances SET Amount = Amount + {0}, UpdatedAt = {1} WHERE AccountId = {2}",
                amount, now, accountId, ct);

            if (rows > 0)
                return;

            try
            {
                await _db.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO Balances (AccountId, Amount, UpdatedAt)
                    SELECT {0}, {1}, {2}
                    WHERE NOT EXISTS (SELECT 1 FROM Balances WHERE AccountId = {0})",
                    accountId, amount, now, ct);
                return;
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is SqlException sqlEx
                      && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
            {
                // Concurrent INSERT won the race — retry the UPDATE on the next iteration
            }
        }

        // Final attempt after exhausting INSERT retries
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Balances SET Amount = Amount + {0}, UpdatedAt = {1} WHERE AccountId = {2}",
            amount, now, accountId, ct);
    }
}
