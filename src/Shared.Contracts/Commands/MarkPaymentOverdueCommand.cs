namespace Shared.Contracts.Commands;

public record MarkPaymentOverdueCommand(Guid PaymentId, Guid LicenseId);
