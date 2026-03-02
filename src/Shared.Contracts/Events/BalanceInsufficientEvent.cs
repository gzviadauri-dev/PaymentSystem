namespace Shared.Contracts.Events;

public record BalanceInsufficientEvent(
    Guid PaymentId,
    decimal BalanceAmount,
    decimal RequiredAmount);
