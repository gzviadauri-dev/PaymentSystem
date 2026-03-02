using MediatR;

namespace Payment.Application.Queries;

public record GetBalanceQuery(Guid AccountId) : IRequest<GetBalanceResult>;

public record GetBalanceResult(Guid AccountId, decimal Amount);
