namespace Payment.Application.Interfaces;

/// <summary>
/// Atomic claim-before-execute store for HTTP idempotency keys.
/// Backed by Redis SET NX — no SQL table, no cleanup service needed.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically claims <paramref name="key"/> with StatusCode=0 (Processing).
    /// Returns <c>true</c> if this call owns the new slot; <c>false</c> if the
    /// key already exists (call <see cref="GetAsync"/> to inspect its state).
    /// </summary>
    Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken ct);

    Task<IdempotencySlot?> GetAsync(string key, CancellationToken ct);

    /// <summary>Updates the slot with the real HTTP response (StatusCode ≥ 100).</summary>
    Task CompleteAsync(string key, int statusCode, string responsePayload,
        TimeSpan ttl, CancellationToken ct);

    /// <summary>Writes the error sentinel (StatusCode = -1) after a command exception.</summary>
    Task FailAsync(string key, string errorPayload, TimeSpan ttl, CancellationToken ct);

    /// <summary>Removes an abandoned slot (StatusCode=0 older than the abandonment window).</summary>
    Task DeleteAbandonedAsync(string key, CancellationToken ct);
}

/// <param name="StatusCode">0=Processing, -1=Failed, ≥100=HTTP result.</param>
/// <param name="Payload">Empty while Processing; error JSON if Failed; response JSON if Complete.</param>
public record IdempotencySlot(int StatusCode, string Payload, DateTimeOffset CreatedAt);
