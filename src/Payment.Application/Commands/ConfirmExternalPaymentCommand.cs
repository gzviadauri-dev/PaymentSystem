using MediatR;

namespace Payment.Application.Commands;

public record ConfirmExternalPaymentCommand(
    Guid PaymentId,
    string ExternalPaymentId,
    string ProviderId,
    decimal Amount,
    string Currency,
    DateTime PaidAt) : IRequest<ConfirmExternalPaymentResult>;

public record ConfirmExternalPaymentResult(bool IsAlreadyProcessed, bool WrongProvider = false, Guid? PaymentId = null);
