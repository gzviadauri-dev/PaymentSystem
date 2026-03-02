namespace Shared.Contracts.Events;

public record PaymentConfirmedEvent(
    Guid PaymentId,
    string Provider,
    DateTime PaidAt);
