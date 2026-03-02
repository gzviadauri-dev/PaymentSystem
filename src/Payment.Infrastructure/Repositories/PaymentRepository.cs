using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Interfaces;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using Shared.Contracts.Events;

namespace Payment.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private const int MaxOutboxPayloadBytes = 65_536; // 64 KB

    private readonly PaymentDbContext _db;

    public PaymentRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<Domain.Entities.Payment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Domain.Entities.Payment>> GetByLicenseIdAsync(
        Guid licenseId, int page, int pageSize, CancellationToken ct = default)
        => await _db.Payments
            .AsNoTracking()
            .Where(p => p.LicenseId == licenseId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<int> CountByLicenseIdAsync(Guid licenseId, CancellationToken ct = default)
        => await _db.Payments.CountAsync(p => p.LicenseId == licenseId, ct);

    public async Task AddAsync(Domain.Entities.Payment payment, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO Payments
              (Id, LicenseId, Amount, Status, Type, ExternalPaymentId, ProviderId,
               TargetId, Month, CreatedAt, PaidAt)
            VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10})",
            payment.Id, payment.LicenseId, payment.Amount,
            payment.Status.ToString(), payment.Type.ToString(),
            (object?)payment.ExternalPaymentId ?? DBNull.Value,
            (object?)payment.ProviderId ?? DBNull.Value,
            (object?)payment.TargetId ?? DBNull.Value,
            (object?)payment.Month ?? DBNull.Value,
            payment.CreatedAt,
            (object?)payment.PaidAt ?? DBNull.Value,
            ct);
    }

    public async Task<Domain.Entities.Payment?> GetByExternalIdAsync(
        string externalPaymentId, string providerId, CancellationToken ct = default)
        => await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.ExternalPaymentId == externalPaymentId &&
                p.ProviderId == providerId, ct);

    /// <inheritdoc/>
    public async Task<ProcessBalancePaymentResult> ProcessBalancePaymentAsync(
        Guid paymentId, Guid accountId, decimal amount, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // AsNoTracking is critical: prevents the change tracker from returning a stale
            // cached snapshot instead of the freshly locked row from the UPDLOCK read.
            var balance = await _db.Balances
                .FromSqlRaw("SELECT * FROM Balances WITH (UPDLOCK, ROWLOCK) WHERE AccountId = {0}", accountId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (balance is null || balance.Amount < amount)
            {
                await tx.RollbackAsync(ct);
                return ProcessBalancePaymentResult.InsufficientBalance;
            }

            var payment = await _db.Payments
                .FromSqlRaw("SELECT * FROM Payments WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", paymentId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (payment is null)
            {
                await tx.RollbackAsync(ct);
                return ProcessBalancePaymentResult.PaymentNotFound;
            }

            if (payment.Status != PaymentStatus.Pending)
            {
                await tx.RollbackAsync(ct);
                return ProcessBalancePaymentResult.PaymentNotPending;
            }

            // All writes via ExecuteSqlRawAsync — never SaveChangesAsync inside a locked transaction
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Balances SET Amount = Amount - {0}, UpdatedAt = {1} WHERE AccountId = {2}",
                amount, DateTime.UtcNow, accountId, ct);

            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Payments SET Status = 'Paid', PaidAt = {0} WHERE Id = {1}",
                DateTime.UtcNow, paymentId, ct);

            await WriteOutboxRawAsync(
                nameof(PaymentCompletedEvent),
                new PaymentCompletedEvent(payment.Id, payment.LicenseId, "Balance", payment.TargetId, payment.Month),
                ct);

            await tx.CommitAsync(ct);
            return ProcessBalancePaymentResult.Success;
        });
    }

    /// <inheritdoc/>
    public async Task<(bool Confirmed, Guid? PaymentId)> TryConfirmExternalAtomicallyAsync(
        string externalPaymentId, string providerId, DateTime paidAt, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var affected = await _db.Database.ExecuteSqlRawAsync(@"
                UPDATE Payments
                SET Status = 'Paid', PaidAt = {0}
                WHERE ExternalPaymentId = {1}
                  AND ProviderId = {2}
                  AND Status = 'Pending'",
                paidAt, externalPaymentId, providerId, ct);

            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                return (false, (Guid?)null);
            }

            var payment = await _db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.ExternalPaymentId == externalPaymentId &&
                    p.ProviderId == providerId, ct);

            if (payment is null)
            {
                await tx.RollbackAsync(ct);
                return (false, (Guid?)null);
            }

            await WriteOutboxRawAsync(
                nameof(PaymentConfirmedEvent),
                new PaymentConfirmedEvent(payment.Id, providerId, paidAt),
                ct);

            await tx.CommitAsync(ct);
            return (true, (Guid?)payment.Id);
        });
    }

    public async Task<bool> MarkOverdueAsync(Guid paymentId, CancellationToken ct = default)
    {
        var rows = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Payments SET Status = 'Overdue' WHERE Id = {0} AND Status = 'Pending'",
            paymentId, ct);
        return rows > 0;
    }

    // Writes an outbox entry via raw SQL so it participates in the current ambient
    // transaction without touching the change tracker.
    private async Task WriteOutboxRawAsync<T>(string type, T payload, CancellationToken ct)
        where T : notnull
    {
        var json = JsonSerializer.Serialize(payload);
        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxOutboxPayloadBytes)
            throw new InvalidOperationException(
                $"Outbox payload for {type} is {byteCount} bytes — exceeds 64 KB limit.");

        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO OutboxMessages (Id, Type, Payload, CreatedAt, RetryCount) VALUES ({0},{1},{2},{3},0)",
            Guid.NewGuid(), type, json, DateTime.UtcNow, ct);
    }
}
