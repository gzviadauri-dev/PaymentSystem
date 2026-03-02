using MediatR;
using Payment.Application.Interfaces;

namespace Payment.Application.Commands;

public class TopUpBalanceCommandHandler : IRequestHandler<TopUpBalanceCommand, TopUpBalanceResult>
{
    private readonly IBalanceRepository _balanceRepo;

    public TopUpBalanceCommandHandler(IBalanceRepository balanceRepo)
    {
        _balanceRepo = balanceRepo;
    }

    public async Task<TopUpBalanceResult> Handle(TopUpBalanceCommand request, CancellationToken ct)
    {
        if (request.Amount <= 0)
            throw new ArgumentException("Top-up amount must be positive.");

        await _balanceRepo.CreditAsync(request.AccountId, request.Amount, ct);
        var balance = await _balanceRepo.GetByAccountIdAsync(request.AccountId, ct);
        return new TopUpBalanceResult(balance?.Amount ?? 0m);
    }
}
