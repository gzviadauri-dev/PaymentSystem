using MediatR;

namespace Payment.Application.Commands;

public record PayViaBalanceCommand(
    Guid PaymentId,
    Guid AccountId,
    decimal Amount) : IRequest<PayViaBalanceResult>;

public record PayViaBalanceResult(bool Success, string? FailureReason = null);
