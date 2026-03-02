namespace LicenseCore.Application.Interfaces;

/// <summary>
/// Creates advisory distributed locks backed by Redis SET NX.
/// Locks are held for <c>lockDuration</c> and renewed every <c>renewalInterval</c>
/// via a Lua heartbeat so they survive brief network hiccups without consuming
/// a SQL Server connection for the lock lifetime.
/// </summary>
public interface IDistributedLockFactory
{
    /// <summary>
    /// Tries to acquire a named lock.
    /// </summary>
    /// <param name="lockName">Human-readable lock name (embedded in the Redis key).</param>
    /// <param name="lockDuration">Initial TTL; renewed automatically every <paramref name="renewalInterval"/>.</param>
    /// <param name="renewalInterval">How often to extend the TTL while the handle is held.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IAsyncDisposable"/> that releases the lock on disposal;
    /// or <c>null</c> if another instance already holds the lock.
    /// </returns>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string lockName,
        TimeSpan lockDuration,
        TimeSpan renewalInterval,
        CancellationToken ct);
}
