namespace Shared.Contracts.Events;

public record MonthlyDebtCreatedEvent(
    Guid PaymentId,
    Guid LicenseId,
    decimal Amount,
    DateTime Month);
