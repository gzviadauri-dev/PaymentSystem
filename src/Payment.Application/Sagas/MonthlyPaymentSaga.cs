using MassTransit;
using Shared.Contracts.Commands;
using Shared.Contracts.Events;

namespace Payment.Application.Sagas;

public class MonthlyPaymentSaga : MassTransitStateMachine<MonthlyPaymentState>
{
    public State AwaitingBalanceCheck { get; private set; } = null!;
    public State AwaitingExternalPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Overdue { get; private set; } = null!;

    public Event<MonthlyDebtCreatedEvent> DebtCreated { get; private set; } = null!;
    public Event<BalanceDebitedEvent> BalanceDebited { get; private set; } = null!;
    public Event<BalanceInsufficientEvent> BalanceInsufficient { get; private set; } = null!;
    public Event<PaymentConfirmedEvent> PaymentConfirmed { get; private set; } = null!;

    public Schedule<MonthlyPaymentState, PaymentTimeoutExpired> PaymentTimeout { get; private set; } = null!;

    public MonthlyPaymentSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => DebtCreated, e => e.CorrelateById(ctx => ctx.Message.PaymentId));
        Event(() => BalanceDebited, e => e.CorrelateById(ctx => ctx.Message.PaymentId));
        Event(() => BalanceInsufficient, e => e.CorrelateById(ctx => ctx.Message.PaymentId));
        Event(() => PaymentConfirmed, e => e.CorrelateById(ctx => ctx.Message.PaymentId));

        Schedule(() => PaymentTimeout, state => state.PaymentTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromDays(30);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Initially(
            When(DebtCreated)
                .Then(ctx =>
                {
                    ctx.Saga.LicenseId = ctx.Message.LicenseId;
                    ctx.Saga.Amount = ctx.Message.Amount;
                    ctx.Saga.Month = ctx.Message.Month;
                    ctx.Saga.CreatedAt = DateTime.UtcNow;
                })
                .TransitionTo(AwaitingBalanceCheck)
                // PaymentId = saga.CorrelationId so TryDebitAndCompletePaymentAsync
                // can atomically update the Payments row inside the debit transaction.
                .Publish(ctx => new TryDebitBalanceCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.LicenseId,
                    ctx.Saga.Amount,
                    ctx.Saga.CorrelationId))
        );

        During(AwaitingBalanceCheck,
            When(BalanceDebited)
                .TransitionTo(Completed)
                .Publish(ctx => new PaymentCompletedEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.LicenseId,
                    "Monthly",
                    null,
                    ctx.Saga.Month))
                .Publish(ctx => new SendNotificationCommand(
                    ctx.Saga.LicenseId,
                    $"Monthly payment of {ctx.Saga.Amount} GEL auto-paid from balance."))
                .Finalize(),

            When(BalanceInsufficient)
                .Then(ctx =>
                {
                    // Store human-readable expiry so the /api/payments/overdue endpoint
                    // can query all expiring sagas without going through the saga engine
                    ctx.Saga.TimeoutAt = DateTime.UtcNow.AddDays(30);
                })
                .TransitionTo(AwaitingExternalPayment)
                .Schedule(PaymentTimeout, ctx => new PaymentTimeoutExpired(ctx.Saga.CorrelationId))
                .Publish(ctx => new SendNotificationCommand(
                    ctx.Saga.LicenseId,
                    $"Monthly debt of {ctx.Saga.Amount} GEL is pending. Please pay via card or mobile bank."))
        );

        During(AwaitingExternalPayment,
            When(PaymentConfirmed)
                .Unschedule(PaymentTimeout)
                .TransitionTo(Completed)
                .Publish(ctx => new PaymentCompletedEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.LicenseId,
                    ctx.Message.Provider,
                    null,
                    ctx.Saga.Month))
                .Publish(ctx => new SendNotificationCommand(
                    ctx.Saga.LicenseId,
                    "Monthly payment received. Thank you!"))
                .Finalize(),

            When(PaymentTimeout!.Received)
                .Then(ctx => ctx.Saga.FailureReason = "Payment overdue after 30 days")
                .TransitionTo(Overdue)
                // Persist Overdue status to the Payments table so the endpoint
                // can query it without touching the saga engine.
                .Publish(ctx => new MarkPaymentOverdueCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.LicenseId))
                .Publish(ctx => new SendNotificationCommand(
                    ctx.Saga.LicenseId,
                    $"Your monthly payment of {ctx.Saga.Amount} GEL is now overdue."))
        );

        SetCompletedWhenFinalized();
    }
}

public record PaymentTimeoutExpired(Guid CorrelationId);
