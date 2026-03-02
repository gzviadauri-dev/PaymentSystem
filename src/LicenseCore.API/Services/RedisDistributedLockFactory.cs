using LicenseCore.Application.Interfaces;
using StackExchange.Redis;

namespace LicenseCore.API.Services;

/// <summary>
/// Redis SET NX distributed lock with Lua-based TTL renewal.
///
/// Lock key:   "lock:{lockName}"
/// Lock value: "{MachineName}-{ProcessId}-{Guid}" — unique per acquisition so only
///             the owner can renew or release (prevents accidental release by another pod).
///
/// Renewal runs every <c>renewalInterval</c> in a background loop using a Lua script
/// that extends the TTL only if the stored value still matches (guards against
/// clock skew or network partitions where the lock expired and was re-acquired).
/// If renewal detects loss, it logs LogCritical and stops — the dispatch run may
/// overlap with another pod, but the per-license INSERT guards prevent double-billing.
/// </summary>
public class RedisDistributedLockFactory : IDistributedLockFactory
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisDistributedLockFactory> _logger;

    public RedisDistributedLockFactory(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisDistributedLockFactory> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string lockName,
        TimeSpan lockDuration,
        TimeSpan renewalInterval,
        CancellationToken ct)
    {
        var lockKey = $"lock:{lockName}";
        var lockValue = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid()}";
        var db = _multiplexer.GetDatabase();

        var acquired = await db.StringSetAsync(lockKey, lockValue, lockDuration, When.NotExists);
        if (!acquired)
            return null;

        return new LockHandle(db, lockKey, lockValue, lockDuration, renewalInterval, _logger);
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _renewalTask;

        // Renew TTL only if this instance still owns the key
        private const string RenewScript = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('pexpire', KEYS[1], ARGV[2])
            else
                return 0
            end";

        // Delete key only if this instance still owns it
        private const string ReleaseScript = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

        public LockHandle(
            IDatabase db,
            string lockKey,
            string lockValue,
            TimeSpan lockDuration,
            TimeSpan renewalInterval,
            ILogger logger)
        {
            _db = db;
            _lockKey = lockKey;
            _lockValue = lockValue;
            _logger = logger;
            _renewalTask = RenewAsync(lockDuration, renewalInterval, _cts.Token);
        }

        private async Task RenewAsync(TimeSpan lockDuration, TimeSpan renewalInterval, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(renewalInterval, ct);
                    if (ct.IsCancellationRequested) break;

                    var renewed = (long)await _db.ScriptEvaluateAsync(RenewScript,
                        new RedisKey[] { _lockKey },
                        new RedisValue[] { _lockValue, (long)lockDuration.TotalMilliseconds });

                    if (renewed == 0)
                    {
                        _logger.LogCritical(
                            "Redis distributed lock {LockKey} was lost during renewal — " +
                            "another instance may now be running the same operation concurrently.",
                            _lockKey);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { /* normal on dispose */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis lock renewal loop failed for {LockKey}", _lockKey);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try { await _renewalTask.ConfigureAwait(false); } catch { /* already logged */ }

            try
            {
                await _db.ScriptEvaluateAsync(ReleaseScript,
                    new RedisKey[] { _lockKey },
                    new RedisValue[] { _lockValue });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release Redis lock {LockKey} — will expire naturally", _lockKey);
            }
        }
    }
}
