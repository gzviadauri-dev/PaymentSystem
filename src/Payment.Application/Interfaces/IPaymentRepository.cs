using Payment.Domain.Enums;

namespace Payment.Application.Interfaces;

public interface IPaymentRepository
{
    Task<Domain.Entities.Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Domain.Entities.Payment>> GetByLicenseIdAsync(
        Guid licenseId, int page, int pageSize, CancellationToken ct = default);

    Task<int> CountByLicenseIdAsync(Guid licenseId, CancellationToken ct = default);

    Task AddAsync(Domain.Entities.Payment payment, CancellationToken ct = default);

    Task<Domain.Entities.Payment?> GetByExternalIdAsync(
        string externalPaymentId, string providerId, CancellationToken ct = default);

    /// <summary>
    /// Atomically: locks balance + payment rows (UPDLOCK), checks preconditions,
    /// debits, marks Paid, writes outbox entry — all raw SQL, single transaction.
    /// </summary>
    Task<ProcessBalancePaymentResult> ProcessBalancePaymentAsync(
        Guid paymentId, Guid accountId, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// Atomically: CAS flip Pending→Paid and writes outbox entry in one transaction.
    /// Filters by Id + LockedProviderId; sets ExternalPaymentId and ProviderId in
    /// the same UPDATE that marks the payment Paid.
    /// </summary>
    Task<ConfirmResult> TryConfirmExternalAtomicallyAsync(
        Guid paymentId, string externalPaymentId, string providerId, CancellationToken ct = default);

    /// <summary>
    /// Marks a payment as Overdue if it is currently Pending. Idempotent — safe on re-delivery.
    /// </summary>
    Task<bool> MarkOverdueAsync(Guid paymentId, CancellationToken ct = default);

    /// <summary>
    /// Marks a payment as Failed (e.g. balance race lost after optimistic check).
    /// </summary>
    Task MarkFailedAsync(Guid paymentId, CancellationToken ct = default);

    /// <summary>
    /// Atomically locks the payment to a provider at redirect time.
    /// Returns Locked if the lock was set (first redirect).
    /// Returns AlreadyLockedSame if already locked to this provider — idempotent redirect retry.
    /// Returns AlreadyLockedOther if locked to a different provider — reject.
    /// </summary>
    Task<LockProviderResult> LockProviderAsync(
        Guid paymentId, string providerId, CancellationToken ct = default);
}

public enum ProcessBalancePaymentResult
{
    Success,
    PaymentNotFound,
    PaymentNotPending,
    InsufficientBalance
}

public enum LockProviderResult
{
    Locked,            // successfully locked now
    AlreadyLockedSame, // was already locked to same provider — idempotent, ok
    AlreadyLockedOther // locked to a different provider — reject
}

public record ConfirmResult(bool IsAlreadyProcessed, bool WrongProvider);
