using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Interfaces;
using Payment.Domain.Entities;
using Payment.Infrastructure.Data;

namespace Payment.Infrastructure.Outbox;

public class OutboxWriter : IOutboxWriter
{
    private const int MaxPayloadBytes = 65_536; // 64 KB

    private readonly PaymentDbContext _db;

    public OutboxWriter(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync<T>(string eventType, T payload, CancellationToken ct = default)
        where T : notnull
    {
        var json = JsonSerializer.Serialize(payload);
        var byteCount = Encoding.UTF8.GetByteCount(json);

        if (byteCount > MaxPayloadBytes)
            throw new InvalidOperationException(
                $"Outbox payload for {eventType} is {byteCount} bytes — exceeds {MaxPayloadBytes / 1024} KB limit. " +
                "Reduce the payload or move large data to a referenced store.");

        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO OutboxMessages (Id, Type, Payload, CreatedAt, RetryCount) VALUES ({0},{1},{2},{3},0)",
            new object[] { Guid.NewGuid(), eventType, json, DateTime.UtcNow }, ct);
    }
}
