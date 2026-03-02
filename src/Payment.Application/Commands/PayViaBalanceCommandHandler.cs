using MediatR;
using Payment.Application.Interfaces;
using Payment.Domain.Enums;

namespace Payment.Application.Commands;

public class PayViaBalanceCommandHandler : IRequestHandler<PayViaBalanceCommand, PayViaBalanceResult>
{
    private readonly IPaymentRepository _paymentRepo;

    public PayViaBalanceCommandHandler(IPaymentRepository paymentRepo)
    {
        _paymentRepo = paymentRepo;
    }

    public async Task<PayViaBalanceResult> Handle(PayViaBalanceCommand request, CancellationToken ct)
    {
        // Single atomic transaction: locks balance + payment rows, debits, marks Paid,
        // and writes the PaymentCompletedEvent outbox entry — no TOCTOU window.
        var result = await _paymentRepo.ProcessBalancePaymentAsync(
            request.PaymentId, request.AccountId, request.Amount, ct);

        return result switch
        {
            ProcessBalancePaymentResult.Success => new PayViaBalanceResult(true),
            ProcessBalancePaymentResult.InsufficientBalance => new PayViaBalanceResult(false, "Insufficient balance."),
            ProcessBalancePaymentResult.PaymentNotFound => new PayViaBalanceResult(false, "Payment not found."),
            ProcessBalancePaymentResult.PaymentNotPending => new PayViaBalanceResult(false, "Payment is already processed."),
            _ => new PayViaBalanceResult(false, "Unknown error.")
        };
    }
}
