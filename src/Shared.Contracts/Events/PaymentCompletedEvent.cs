namespace Shared.Contracts.Events;

public record PaymentCompletedEvent(
    Guid PaymentId,
    Guid LicenseId,
    string PaymentType,
    Guid? TargetId = null,
    DateTime? Month = null);
