using MassTransit;
using Microsoft.Extensions.Logging;
using Payment.Application.Interfaces;
using Shared.Contracts.Commands;

namespace Payment.Application.Consumers;

public class MarkPaymentOverdueConsumer : IConsumer<MarkPaymentOverdueCommand>
{
    private readonly IPaymentRepository _paymentRepo;
    private readonly ILogger<MarkPaymentOverdueConsumer> _logger;

    public MarkPaymentOverdueConsumer(
        IPaymentRepository paymentRepo,
        ILogger<MarkPaymentOverdueConsumer> logger)
    {
        _paymentRepo = paymentRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MarkPaymentOverdueCommand> context)
    {
        var cmd = context.Message;

        var marked = await _paymentRepo.MarkOverdueAsync(cmd.PaymentId, context.CancellationToken);

        if (marked)
            _logger.LogInformation(
                "Marked payment {PaymentId} (license {LicenseId}) as Overdue",
                cmd.PaymentId, cmd.LicenseId);
        else
            _logger.LogWarning(
                "Payment {PaymentId} was not in Pending state when overdue mark arrived — possible duplicate",
                cmd.PaymentId);
    }
}
