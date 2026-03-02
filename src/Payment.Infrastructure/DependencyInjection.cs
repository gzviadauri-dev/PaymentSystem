using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.Application.Interfaces;
using Payment.Infrastructure.Data;
using Payment.Infrastructure.Idempotency;
using Payment.Infrastructure.MassTransit;
using Payment.Infrastructure.Outbox;
using Payment.Infrastructure.Repositories;
using StackExchange.Redis;

namespace Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(30);
                }));

        // Redis: shared distributed cache + connection multiplexer for IIdempotencyStore
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string not configured.");

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "PaymentSystem:";
        });

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnection));

        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

        services.AddScoped<IBalanceRepository, BalanceRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        services.AddHostedService<OutboxProcessor>();

        services.AddMassTransitWithRabbitMq(configuration);

        return services;
    }
}
