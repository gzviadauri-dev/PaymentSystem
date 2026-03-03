using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payment.Domain.Entities;
using Payment.Infrastructure.Data;
using Shared.Contracts.Events;

namespace Payment.Infrastructure.Outbox;

public class OutboxProcessor : BackgroundService
{
    // Unique per running process — prevents two instances from claiming the same batch
    private static readonly string InstanceId =
        $"{Environment.MachineName}-{Environment.ProcessId}";

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OutboxProcessor batch failed");
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var leaseExpiry = DateTime.UtcNow.Add(LeaseDuration);
        var now = DateTime.UtcNow;

        // Step 1: Atomically claim up to 20 unclaimed (or lease-expired) rows.
        // Only this instance's InstanceId will be set, so the follow-up SELECT
        // is safe even if another instance runs concurrently.
        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE TOP (20) OutboxMessages
            SET LockedUntil = {0}, LockedBy = {1}
            WHERE ProcessedAt IS NULL
              AND RetryCount < 5
              AND (LockedUntil IS NULL OR LockedUntil < {2})",
            new object[] { leaseExpiry, InstanceId, now }, ct);

        // Step 2: Fetch only the rows this instance just claimed
        var messages = await db.OutboxMessages
            .Where(m => m.LockedBy == InstanceId && m.ProcessedAt == null)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        foreach (var msg in messages)
        {
            try
            {
                var envelope = DeserializeMessage(msg);
                if (envelope is not null)
                    await bus.Publish(envelope, ct);

                // Step 3: CAS on ProcessedAt + clear lease — prevents double-processing
                // if the service crashed after Publish but before this update.
                var updated = await db.Database.ExecuteSqlRawAsync(@"
                    UPDATE OutboxMessages
                    SET ProcessedAt = {0}, LockedBy = NULL, LockedUntil = NULL
                    WHERE Id = {1} AND ProcessedAt IS NULL AND LockedBy = {2}",
                    new object[] { DateTime.UtcNow, msg.Id, InstanceId }, ct);

                if (updated == 0)
                {
                    _logger.LogWarning(
                        "Outbox message {Id} was already processed by another instance", msg.Id);
                }
                else
                {
                    // Mark any DeadLetterMessage that was replayed via this outbox entry as Succeeded
                    await db.Database.ExecuteSqlRawAsync(@"
                        UPDATE DeadLetterMessages
                        SET Status = 'Succeeded'
                        WHERE ReplayedOutboxMessageId = {0} AND Status = 'Replaying'",
                        new object[] { msg.Id }, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {Id}", msg.Id);

                var newRetryCount = msg.RetryCount + 1;
                await db.Database.ExecuteSqlRawAsync(@"
                    UPDATE OutboxMessages
                    SET RetryCount = {0}, Error = {1}, LockedBy = NULL, LockedUntil = NULL
                    WHERE Id = {2} AND LockedBy = {3}",
                    new object[] { newRetryCount, ex.Message[..Math.Min(ex.Message.Length, 500)],
                        msg.Id, InstanceId }, ct);

                if (newRetryCount >= 5)
                    await DeadLetterAsync(db, msg, ex.Message, ct);
            }
        }
    }

    private static async Task DeadLetterAsync(
        PaymentDbContext db, OutboxMessage msg, string error, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO DeadLetterMessages
              (Id, OriginalOutboxMessageId, Type, Payload, LastError, RetryCount,
               OriginalCreatedAt, DeadLetteredAt, Status)
            VALUES ({0},{1},{2},{3},{4},{5},{6},{7},'Pending')",
            new object[] { Guid.NewGuid(), msg.Id, msg.Type, msg.Payload,
                error[..Math.Min(error.Length, 2000)],
                msg.RetryCount, msg.CreatedAt, DateTime.UtcNow }, ct);
    }

    private static object? DeserializeMessage(OutboxMessage msg)
    {
        return msg.Type switch
        {
            nameof(PaymentConfirmedEvent) =>
                JsonSerializer.Deserialize<PaymentConfirmedEvent>(msg.Payload),
            nameof(PaymentCompletedEvent) =>
                JsonSerializer.Deserialize<PaymentCompletedEvent>(msg.Payload),
            nameof(MonthlyDebtCreatedEvent) =>
                JsonSerializer.Deserialize<MonthlyDebtCreatedEvent>(msg.Payload),
            nameof(BalanceDebitedEvent) =>
                JsonSerializer.Deserialize<BalanceDebitedEvent>(msg.Payload),
            nameof(BalanceInsufficientEvent) =>
                JsonSerializer.Deserialize<BalanceInsufficientEvent>(msg.Payload),
            _ => null
        };
    }
}
