namespace Payment.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }

    /// <summary>
    /// Set to now+30s when a processor instance claims this row.
    /// Rows whose lease has expired are eligible for reclaiming.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// "{MachineName}-{ProcessId}" of the claiming processor instance.
    /// Null when the row is unclaimed or after successful processing.
    /// </summary>
    public string? LockedBy { get; set; }
}
