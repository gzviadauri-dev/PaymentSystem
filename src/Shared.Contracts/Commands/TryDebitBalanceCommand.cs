namespace Shared.Contracts.Commands;

public record TryDebitBalanceCommand(
    Guid CorrelationId,
    Guid AccountId,
    decimal Amount,
    Guid PaymentId);
