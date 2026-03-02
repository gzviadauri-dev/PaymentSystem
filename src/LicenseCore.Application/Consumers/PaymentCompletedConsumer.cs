using LicenseCore.Application.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Events;

namespace LicenseCore.Application.Consumers;

public class PaymentCompletedConsumer : IConsumer<PaymentCompletedEvent>
{
    private readonly ILicenseRepository _licenseRepo;
    private readonly ILogger<PaymentCompletedConsumer> _logger;

    public PaymentCompletedConsumer(
        ILicenseRepository licenseRepo,
        ILogger<PaymentCompletedConsumer> logger)
    {
        _licenseRepo = licenseRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "PaymentCompletedEvent: Payment={PaymentId}, License={LicenseId}, Type={Type}",
            evt.PaymentId, evt.LicenseId, evt.PaymentType);

        // All repository methods use WHERE IsActive = 0/1 guards — idempotent on re-delivery
        switch (evt.PaymentType)
        {
            case "AddVehicle":
                await _licenseRepo.ActivateVehicleForLicenseAsync(evt.LicenseId, ct);
                break;

            case "AddDriver" when evt.TargetId.HasValue:
                await _licenseRepo.ActivateDriverAsync(evt.TargetId.Value, evt.LicenseId, ct);
                break;

            case "Monthly":
                await _licenseRepo.MarkMonthPaidAsync(evt.LicenseId, evt.Month, ct);
                break;

            case "LicenseSell":
                await _licenseRepo.ActivateLicenseAsync(evt.LicenseId, ct);
                break;

            case "LicenceCancel":
                await _licenseRepo.DeactivateLicenseAsync(evt.LicenseId, ct);
                break;

            default:
                _logger.LogWarning(
                    "Unknown PaymentType '{Type}' for PaymentId={PaymentId} — skipped",
                    evt.PaymentType, evt.PaymentId);
                break;
        }
    }
}
