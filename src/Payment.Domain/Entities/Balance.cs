namespace Payment.Domain.Entities;

public class Balance
{
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
