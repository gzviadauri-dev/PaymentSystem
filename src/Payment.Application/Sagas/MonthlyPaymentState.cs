using MassTransit;

namespace Payment.Application.Sagas;

public class MonthlyPaymentState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid LicenseId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Month { get; set; }
    public string? FailureReason { get; set; }
    public Guid? PaymentTimeoutTokenId { get; set; }

    /// <summary>Timestamp when this saga was created (DebtCreated received).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the 30-day grace period expires. Set when transitioning to
    /// AwaitingExternalPayment. Queryable without going through the saga engine.
    /// </summary>
    public DateTime? TimeoutAt { get; set; }
}
