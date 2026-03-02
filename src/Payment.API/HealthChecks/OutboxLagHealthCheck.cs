using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payment.Infrastructure.Data;

namespace Payment.API.HealthChecks;

/// <summary>
/// Reports Degraded when outbox messages have been sitting unprocessed for > 5 minutes,
/// and Unhealthy when the lag exceeds 30 minutes. Uses Degraded (not Unhealthy) at the
/// short threshold so the pod stays in the load balancer while monitoring alerts fire.
///
/// Results are cached for 30 seconds in Redis (IDistributedCache), shared across all pod
/// replicas. With N pods, the COUNT(*) query executes once per 30-second window across
/// the entire cluster, not once per pod per window — eliminating shared-lock contention
/// against OutboxProcessor's UPDLOCK queries.
/// </summary>
public class OutboxLagHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;

    private static readonly TimeSpan DegradedThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UnhealthyThreshold = TimeSpan.FromMinutes(30);
    private const string CacheKey = "outbox-lag-health";
    private const int CacheTtlSeconds = 30;

    public OutboxLagHealthCheck(IServiceScopeFactory scopeFactory, IDistributedCache cache)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        // Shared Redis cache — one query per 30-second window for the whole cluster
        try
        {
            var cached = await _cache.GetStringAsync(CacheKey, ct);
            if (cached != null)
                return DeserializeResult(cached);
        }
        catch
        {
            // Redis miss (unavailable) — fall through to live query
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var unhealthyBefore = DateTime.UtcNow - UnhealthyThreshold;
        var degradedBefore = DateTime.UtcNow - DegradedThreshold;

        var criticalCount = await db.OutboxMessages
            .CountAsync(m => m.ProcessedAt == null
                          && m.RetryCount < 5
                          && m.CreatedAt < unhealthyBefore, ct);

        HealthCheckResult result;
        if (criticalCount > 0)
        {
            result = HealthCheckResult.Unhealthy(
                $"{criticalCount} outbox message(s) unprocessed for > 30 minutes. " +
                "OutboxProcessor appears to be down or RabbitMQ connection is broken.");
        }
        else
        {
            var staleCount = await db.OutboxMessages
                .CountAsync(m => m.ProcessedAt == null
                              && m.RetryCount < 5
                              && m.CreatedAt < degradedBefore, ct);

            result = staleCount == 0
                ? HealthCheckResult.Healthy("Outbox processor is current.")
                : HealthCheckResult.Degraded(
                    $"{staleCount} outbox message(s) unprocessed for > 5 minutes. " +
                    "OutboxProcessor may be slow or temporarily unable to reach RabbitMQ.");
        }

        try
        {
            await _cache.SetStringAsync(
                CacheKey,
                SerializeResult(result),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTtlSeconds)
                },
                ct);
        }
        catch
        {
            // Redis write failure is non-fatal; next call will re-query the database
        }

        return result;
    }

    private record CachedResult(int Status, string Description);

    private static string SerializeResult(HealthCheckResult r) =>
        JsonSerializer.Serialize(new CachedResult((int)r.Status, r.Description ?? ""));

    private static HealthCheckResult DeserializeResult(string json)
    {
        var r = JsonSerializer.Deserialize<CachedResult>(json)!;
        return (HealthStatus)r.Status switch
        {
            HealthStatus.Healthy => HealthCheckResult.Healthy(r.Description),
            HealthStatus.Degraded => HealthCheckResult.Degraded(r.Description),
            _ => HealthCheckResult.Unhealthy(r.Description),
        };
    }
}
