using MediatR;

namespace Payment.Application.Commands;

public record TopUpBalanceCommand(Guid AccountId, decimal Amount) : IRequest<TopUpBalanceResult>;

public record TopUpBalanceResult(decimal NewBalance);
