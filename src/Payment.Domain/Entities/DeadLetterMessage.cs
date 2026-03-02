namespace Payment.Domain.Entities;

public enum DeadLetterStatus { Pending, Replaying, Succeeded, Failed }

/// <summary>
/// Messages moved here after OutboxProcessor exhausts all retries (RetryCount >= 5).
/// Visible via /api/admin/dead-letters and replayable via /api/admin/dead-letters/{id}/replay.
/// </summary>
public class DeadLetterMessage
{
    public Guid Id { get; set; }
    public Guid OriginalOutboxMessageId { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public string LastError { get; set; } = null!;
    public int RetryCount { get; set; }
    public DateTime OriginalCreatedAt { get; set; }
    public DateTime DeadLetteredAt { get; set; }
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;
    public Guid? ReplayedOutboxMessageId { get; set; }
    public DateTime? ReplayedAt { get; set; }
    public string? ReplayError { get; set; }
}
