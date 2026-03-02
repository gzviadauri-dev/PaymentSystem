using MediatR;
using Payment.Domain.Enums;

namespace Payment.Application.Commands;

public record CreatePaymentCommand(
    Guid LicenseId,
    decimal Amount,
    PaymentType Type,
    string? ExternalPaymentId,
    string? ProviderId,
    Guid? TargetId) : IRequest<CreatePaymentResult>;

public record CreatePaymentResult(Guid Id, Guid LicenseId);
