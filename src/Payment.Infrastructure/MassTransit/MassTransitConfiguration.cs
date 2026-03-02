using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.Application.Consumers;
using Payment.Application.Sagas;
using Payment.Infrastructure.Data;

namespace Payment.Infrastructure.MassTransit;

public static class MassTransitConfiguration
{
    public static IServiceCollection AddMassTransitWithRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<PaymentDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
            });

            x.AddSagaStateMachine<MonthlyPaymentSaga, MonthlyPaymentState>()
             .EntityFrameworkRepository(r =>
             {
                 r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                 r.AddDbContext<DbContext, PaymentDbContext>((provider, builder) =>
                 {
                     builder.UseSqlServer(
                         configuration.GetConnectionString("DefaultConnection"));
                 });
                 r.UseSqlServer();
             });

            x.AddConsumer<TryDebitBalanceConsumer>();
            // CoreApiNotifierConsumer removed: its responsibilities are covered by
            // PaymentCompletedConsumer in LicenseCore.API (fan-out via RabbitMQ).
            x.AddConsumer<MarkPaymentOverdueConsumer>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var rabbitHost = configuration["RabbitMQ:Host"] ?? "rabbitmq://localhost";
                cfg.Host(rabbitHost);

                cfg.UseMessageRetry(r => r.Exponential(
                    5,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(5)));

                cfg.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }
}
