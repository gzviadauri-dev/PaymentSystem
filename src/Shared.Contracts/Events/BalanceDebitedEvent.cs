namespace Shared.Contracts.Events;

public record BalanceDebitedEvent(
    Guid PaymentId,
    decimal Amount);
