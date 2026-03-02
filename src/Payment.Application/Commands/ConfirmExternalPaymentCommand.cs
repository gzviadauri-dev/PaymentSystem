using MediatR;

namespace Payment.Application.Commands;

public record ConfirmExternalPaymentCommand(
    string ExternalPaymentId,
    string ProviderId,
    decimal Amount,
    string Currency,
    DateTime PaidAt) : IRequest<ConfirmExternalPaymentResult>;

public record ConfirmExternalPaymentResult(bool IsAlreadyProcessed, Guid? PaymentId = null);
