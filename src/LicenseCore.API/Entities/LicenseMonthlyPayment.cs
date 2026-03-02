namespace LicenseCore.API.Entities;

/// <summary>
/// Tracks which calendar months have been paid for a license.
/// Used by LicenseService.MarkMonthPaidAsync to prevent duplicate activations.
/// </summary>
public class LicenseMonthlyPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public License License { get; set; } = null!;

    /// <summary>Normalised to the 1st of the month, UTC.</summary>
    public DateTime Month { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
