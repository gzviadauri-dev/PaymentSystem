using MassTransit;
using Microsoft.Extensions.Logging;
using Payment.Application.Interfaces;
using Shared.Contracts.Commands;
using Shared.Contracts.Events;

namespace Payment.Application.Consumers;

public class TryDebitBalanceConsumer : IConsumer<TryDebitBalanceCommand>
{
    private readonly IBalanceRepository _balanceRepo;
    private readonly ILogger<TryDebitBalanceConsumer> _logger;

    public TryDebitBalanceConsumer(
        IBalanceRepository balanceRepo,
        ILogger<TryDebitBalanceConsumer> logger)
    {
        _balanceRepo = balanceRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TryDebitBalanceCommand> context)
    {
        var cmd = context.Message;
        var ct = context.CancellationToken;

        try
        {
            var result = await _balanceRepo.TryDebitAndCompletePaymentAsync(
                cmd.AccountId, cmd.PaymentId, cmd.Amount, ct);

            switch (result.Status)
            {
                case TryDebitResult.Success:
                    // BalanceDebitedEvent was written to the outbox inside the atomic transaction.
                    // OutboxProcessor will publish it to RabbitMQ — no manual publish needed here.
                    break;

                case TryDebitResult.InsufficientBalance:
                    await context.Publish(new BalanceInsufficientEvent(
                        cmd.CorrelationId, result.CurrentBalance, cmd.Amount));
                    break;

                case TryDebitResult.AlreadyProcessed:
                    _logger.LogWarning(
                        "Payment {PaymentId} was not in Pending state; acking duplicate debit command",
                        cmd.PaymentId);
                    break;
            }
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // A unique constraint violation means this is a true duplicate — ack the message
            // rather than retrying endlessly. The first delivery already processed it correctly.
            _logger.LogWarning(ex,
                "Unique constraint violation for payment {PaymentId} — likely duplicate; acking",
                cmd.PaymentId);
        }
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        // Walk the inner exception chain to find a SqlException with code 2627 or 2601
        var inner = ex;
        while (inner is not null)
        {
            if (inner.GetType().Name == "SqlException")
            {
                var numberProp = inner.GetType().GetProperty("Number");
                if (numberProp?.GetValue(inner) is int number && (number == 2627 || number == 2601))
                    return true;
            }

            inner = inner.InnerException;
        }

        return false;
    }
}
