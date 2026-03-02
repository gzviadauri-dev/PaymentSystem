namespace LicenseCore.API.Entities;

/// <summary>
/// Records which (LicenseId, Month) pairs have had a MonthlyDebtCreatedEvent published.
/// The unique index prevents duplicate debt events if MonthlyDebtGeneratorService
/// restarts or crashes mid-run on the 1st of the month.
/// </summary>
public class MonthlyDebtDispatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public License License { get; set; } = null!;

    /// <summary>Normalised to the 1st of the month, UTC.</summary>
    public DateTime Month { get; set; }
    public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;
}
