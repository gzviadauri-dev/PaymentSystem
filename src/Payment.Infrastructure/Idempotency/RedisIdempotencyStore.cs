using System.Text.Json;
using Payment.Application.Interfaces;
using StackExchange.Redis;

namespace Payment.Infrastructure.Idempotency;

/// <summary>
/// Redis-backed idempotency store using SET NX for atomic slot claiming.
/// Keys live under the instance prefix with a 24-hour TTL — no cleanup service needed.
///
/// Key format: "PaymentSystem:{endpoint}:{clientKey}"
/// Value:      JSON blob of { StatusCode, Payload, CreatedAt }
/// </summary>
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _multiplexer;

    // Lua: update only if the key exists (acts like UPDATE WHERE key=X)
    private const string SetIfExistsScript = @"
        if redis.call('exists', KEYS[1]) == 1 then
            return redis.call('set', KEYS[1], ARGV[1], 'px', ARGV[2])
        else
            return nil
        end";

    public RedisIdempotencyStore(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var db = _multiplexer.GetDatabase();
        var slot = Serialize(new IdempotencySlot(0, "", DateTimeOffset.UtcNow));
        // SET NX PX — atomic: returns true only if key did not exist
        return await db.StringSetAsync(key, slot, ttl, When.NotExists);
    }

    public async Task<IdempotencySlot?> GetAsync(string key, CancellationToken ct)
    {
        var db = _multiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);
        return value.IsNullOrEmpty ? null : Deserialize(value!);
    }

    public async Task CompleteAsync(string key, int statusCode, string responsePayload,
        TimeSpan ttl, CancellationToken ct)
    {
        var db = _multiplexer.GetDatabase();
        var slot = Serialize(new IdempotencySlot(statusCode, responsePayload, DateTimeOffset.UtcNow));
        await db.ScriptEvaluateAsync(SetIfExistsScript,
            new RedisKey[] { key },
            new RedisValue[] { slot, (long)ttl.TotalMilliseconds });
    }

    public async Task FailAsync(string key, string errorPayload, TimeSpan ttl, CancellationToken ct)
    {
        var db = _multiplexer.GetDatabase();
        var slot = Serialize(new IdempotencySlot(-1, errorPayload, DateTimeOffset.UtcNow));
        await db.ScriptEvaluateAsync(SetIfExistsScript,
            new RedisKey[] { key },
            new RedisValue[] { slot, (long)ttl.TotalMilliseconds });
    }

    public async Task DeleteAbandonedAsync(string key, CancellationToken ct)
    {
        var db = _multiplexer.GetDatabase();
        await db.KeyDeleteAsync(key);
    }

    private static string Serialize(IdempotencySlot slot) => JsonSerializer.Serialize(slot);

    private static IdempotencySlot Deserialize(string json) =>
        JsonSerializer.Deserialize<IdempotencySlot>(json)!;
}
