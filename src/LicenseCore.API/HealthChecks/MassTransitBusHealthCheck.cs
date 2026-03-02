using MassTransit;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LicenseCore.API.HealthChecks;

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

        return Task.FromResult(
            _bus.Address is not null
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Bus has no address — not started"));
    }
}
