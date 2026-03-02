using MediatR;
using Payment.Application.Commands;
using Payment.Application.Queries;

namespace Payment.API.Endpoints;

public static class BalanceEndpoints
{
    public static void MapBalanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/balance").WithTags("Balance").RequireAuthorization();

        group.MapGet("/{accountId:guid}", async (Guid accountId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetBalanceQuery(accountId), ct);
            return Results.Ok(result);
        })
        .WithName("GetBalance");

        group.MapPost("/topup", async (TopUpRequest req, IMediator mediator, CancellationToken ct) =>
        {
            if (req.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be positive." });

            var result = await mediator.Send(new TopUpBalanceCommand(req.AccountId, req.Amount), ct);
            return Results.Ok(result);
        })
        .WithName("TopUpBalance");
    }
}

public record TopUpRequest(Guid AccountId, decimal Amount);
