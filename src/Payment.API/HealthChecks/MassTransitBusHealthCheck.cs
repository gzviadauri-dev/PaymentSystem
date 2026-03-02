using MassTransit;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Payment.API.HealthChecks;

/// <summary>
/// Checks that the MassTransit bus is fully started and its RabbitMQ connection is
/// established — not just a TCP-level ping. A misconfigured topology (e.g. missing
/// exchange after RabbitMQ restart) fails here rather than silently dropping messages.
/// </summary>
public class MassTransitBusHealthCheck : IHealthCheck
{
    private readonly IBus _bus;

    public MassTransitBusHealthCheck(IBus bus)
    {
        _bus = bus;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_bus is IBusControl control)
        {
            var health = control.CheckHealth();
            return Task.FromResult(
                health.Status == BusHealthStatus.Healthy
                    ? HealthCheckResult.Healthy($"MassTransit bus healthy. Address: {_bus.Address}")
                    : HealthCheckResult.Unhealthy($"MassTransit bus status: {health.Status}"));
        }

        // Fallback for cases where IBusControl is not available
        return Task.FromResult(
            _bus.Address is not null
                ? HealthCheckResult.Healthy($"Bus address: {_bus.Address}")
                : HealthCheckResult.Unhealthy("Bus has no address — not started"));
    }
}
