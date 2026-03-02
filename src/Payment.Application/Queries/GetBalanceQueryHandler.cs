using MediatR;
using Payment.Application.Interfaces;

namespace Payment.Application.Queries;

public class GetBalanceQueryHandler : IRequestHandler<GetBalanceQuery, GetBalanceResult>
{
    private readonly IBalanceRepository _balanceRepo;

    public GetBalanceQueryHandler(IBalanceRepository balanceRepo)
    {
        _balanceRepo = balanceRepo;
    }

    public async Task<GetBalanceResult> Handle(GetBalanceQuery request, CancellationToken ct)
    {
        var balance = await _balanceRepo.GetByAccountIdAsync(request.AccountId, ct);
        return new GetBalanceResult(request.AccountId, balance?.Amount ?? 0m);
    }
}
