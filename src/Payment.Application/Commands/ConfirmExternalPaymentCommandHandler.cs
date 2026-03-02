using MediatR;
using Payment.Application.Interfaces;

namespace Payment.Application.Commands;

public class ConfirmExternalPaymentCommandHandler
    : IRequestHandler<ConfirmExternalPaymentCommand, ConfirmExternalPaymentResult>
{
    private readonly IPaymentRepository _paymentRepo;

    public ConfirmExternalPaymentCommandHandler(IPaymentRepository paymentRepo)
    {
        _paymentRepo = paymentRepo;
    }

    public async Task<ConfirmExternalPaymentResult> Handle(
        ConfirmExternalPaymentCommand cmd,
        CancellationToken ct)
    {
        // Single atomic transaction: flips Pending→Paid (CAS) and writes the
        // PaymentConfirmedEvent outbox entry in one commit — crash-safe.
        var (confirmed, paymentId) = await _paymentRepo.TryConfirmExternalAtomicallyAsync(
            cmd.ExternalPaymentId,
            cmd.ProviderId,
            cmd.PaidAt,
            ct);

        return confirmed
            ? new ConfirmExternalPaymentResult(IsAlreadyProcessed: false, PaymentId: paymentId)
            : new ConfirmExternalPaymentResult(IsAlreadyProcessed: true);
    }
}
