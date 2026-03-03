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
        var result = await _paymentRepo.TryConfirmExternalAtomicallyAsync(
            cmd.PaymentId,
            cmd.ExternalPaymentId,
            cmd.ProviderId,
            ct);

        return new ConfirmExternalPaymentResult(
            IsAlreadyProcessed: result.IsAlreadyProcessed,
            WrongProvider: result.WrongProvider);
    }
}
