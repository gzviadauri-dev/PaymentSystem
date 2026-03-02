using Payment.Domain.Entities;

namespace Payment.Application.Interfaces;

public enum TryDebitResult { Success, InsufficientBalance, AlreadyProcessed }

/// <summary>
/// Result of <see cref="IBalanceRepository.TryDebitAndCompletePaymentAsync"/>.
/// <see cref="CurrentBalance"/> is populated only when <see cref="Status"/> is
/// <see cref="TryDebitResult.InsufficientBalance"/>.
/// </summary>
public readonly record struct TryDebitAndCompletePaymentResult(TryDebitResult Status, decimal CurrentBalance);

public interface IBalanceRepository
{
    Task<Balance?> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);
    Task<Balance> GetOrCreateAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Atomically within a single ReadCommitted transaction:
    /// lock Balance + Payment rows (UPDLOCK), debit balance, mark payment Paid,
    /// and write a BalanceDebitedEvent to the custom outbox.
    /// </summary>
    Task<TryDebitAndCompletePaymentResult> TryDebitAndCompletePaymentAsync(
        Guid accountId, Guid paymentId, decimal amount, CancellationToken ct = default);

    Task CreditAsync(Guid accountId, decimal amount, CancellationToken ct = default);
}
