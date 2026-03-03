using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using Payment.Infrastructure.Data;

namespace Payment.API.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization("AdminPolicy");

        // ── GET /api/admin/dead-letters ─────────────────────────────────────
        group.MapGet("/dead-letters", async (
            PaymentDbContext db,
            CancellationToken ct,
            int limit = 100) =>
        {
            var capped = Math.Clamp(limit, 1, 500);
            var items = await db.DeadLetterMessages
                .AsNoTracking()
                .Where(d => d.Status == DeadLetterStatus.Pending)
                .OrderByDescending(d => d.DeadLetteredAt)
                .Take(capped)
                .Select(d => new
                {
                    d.Id,
                    d.OriginalOutboxMessageId,
                    d.Type,
                    d.LastError,
                    d.RetryCount,
                    d.OriginalCreatedAt,
                    d.DeadLetteredAt,
                    Status = d.Status.ToString()
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        }).WithName("GetDeadLetters");

        // ── POST /api/admin/dead-letters/{id}/replay ────────────────────────
        // Re-inserts the payload into OutboxMessages with RetryCount=0 so
        // OutboxProcessor picks it up on the next poll cycle.
        // Sets Status = Replaying and records the new outbox entry ID so
        // OutboxProcessor can mark it Succeeded when delivery confirms.
        group.MapPost("/dead-letters/{id:guid}/replay", async (
            Guid id,
            PaymentDbContext db,
            CancellationToken ct) =>
        {
            var dead = await db.DeadLetterMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (dead is null)
                return Results.NotFound(new { error = "Dead-letter message not found." });

            if (dead.Status == DeadLetterStatus.Replaying)
                return Results.Conflict(new { error = "Replay already in progress.", replayedAt = dead.ReplayedAt });

            if (dead.Status != DeadLetterStatus.Pending)
                return Results.Conflict(new
                {
                    error = $"Cannot replay a message with status '{dead.Status}'.",
                    status = dead.Status.ToString()
                });

            var newOutboxId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Re-inject into outbox
            await db.Database.ExecuteSqlRawAsync(@"
                INSERT INTO OutboxMessages (Id, Type, Payload, CreatedAt, RetryCount)
                VALUES ({0},{1},{2},{3},0)",
                new object[] { newOutboxId, dead.Type, dead.Payload, now }, ct);

            // Mark as Replaying with the new outbox entry's ID
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE DeadLetterMessages
                SET Status = 'Replaying', ReplayedOutboxMessageId = {0}, ReplayedAt = {1}
                WHERE Id = {2}",
                new object[] { newOutboxId, now, id }, ct);

            return Results.Ok(new { replayed = true, newOutboxEntryId = newOutboxId });
        }).WithName("ReplayDeadLetter");
    }
}
