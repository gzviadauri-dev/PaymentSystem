using Payment.Domain.Enums;

namespace Payment.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentType Type { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? ProviderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? Month { get; set; }
    public Guid? TargetId { get; set; }
}
