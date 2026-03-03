using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Commands;
using Payment.Application.Interfaces;
using Payment.Application.Queries;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using StackExchange.Redis;

namespace Payment.API.Endpoints;

public static class PaymentEndpoints
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan AbandonmentWindow = TimeSpan.FromSeconds(30);

    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/payments").WithTags("Payments").RequireAuthorization();

        // ── GET /api/payments/{licenseId}?page=1&pageSize=20 ────────────────
        group.MapGet("/{licenseId:guid}", async (
            Guid licenseId,
            IMediator mediator,
            CancellationToken ct,
            int page = 1,
            int pageSize = 20) =>
        {
            var result = await mediator.Send(
                new GetPaymentsQuery(licenseId, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), ct);
            return Results.Ok(result);
        })
        .WithName("GetPayments");

        // ── POST /api/payments/pay-via-balance ──────────────────────────────
        group.MapPost("/pay-via-balance", async (
            PayViaBalanceRequest req,
            IMediator mediator,
            IIdempotencyStore idempotencyStore,
            HttpRequest httpReq,
            CancellationToken ct) =>
        {
            if (!httpReq.Headers.TryGetValue("Idempotency-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
                return Results.BadRequest(new { error = "Idempotency-Key header is required." });

            var key = $"pay-via-balance:{rawKey}";

            // Step 1: Atomic Redis SET NX — claims the slot if key does not exist
            bool claimed;
            try
            {
                claimed = await idempotencyStore.TryClaimAsync(key, IdempotencyTtl, ct);
            }
            catch (RedisException)
            {
                return Results.StatusCode(503);
            }

            if (!claimed)
            {
                var existing = await idempotencyStore.GetAsync(key, ct);

                if (existing is not null)
                {
                    // StatusCode=-1: command threw — return stored error; client must use a new key
                    if (existing.StatusCode == -1)
                        return Results.Json(
                            JsonSerializer.Deserialize<object>(existing.Payload),
                            statusCode: 500);

                    // Completed — return the exact same cached response
                    if (existing.StatusCode >= 100)
                        return Results.Json(
                            JsonSerializer.Deserialize<object>(existing.Payload),
                            statusCode: existing.StatusCode);

                    // StatusCode=0: slot is in-flight or abandoned
                    if (existing.CreatedAt < DateTimeOffset.UtcNow - AbandonmentWindow)
                    {
                        // Crash-abandoned slot — delete and reclaim
                        await idempotencyStore.DeleteAbandonedAsync(key, ct);

                        try
                        {
                            claimed = await idempotencyStore.TryClaimAsync(key, IdempotencyTtl, ct);
                        }
                        catch (RedisException)
                        {
                            return Results.StatusCode(503);
                        }

                        if (!claimed)
                        {
                            httpReq.HttpContext.Response.Headers.Append("Retry-After", "2");
                            return Results.Json(
                                new { error = "processing", retryAfterSeconds = 2 },
                                statusCode: 409);
                        }
                        // Slot reclaimed — fall through to command execution
                    }
                    else
                    {
                        httpReq.HttpContext.Response.Headers.Append("Retry-After", "2");
                        return Results.Json(
                            new { error = "processing", retryAfterSeconds = 2 },
                            statusCode: 409);
                    }
                }
            }

            // Step 2: This request owns the slot — execute the command
            try
            {
                var result = await mediator.Send(
                    new PayViaBalanceCommand(req.PaymentId, req.AccountId, req.Amount), ct);

                var responseBody = result.Success
                    ? (object)new { success = true }
                    : new { success = false, reason = result.FailureReason };
                var statusCode = result.Success ? 200 : 422;

                // Step 3: Store result in Redis so future retries get the cached response
                await idempotencyStore.CompleteAsync(key, statusCode,
                    JsonSerializer.Serialize(responseBody), IdempotencyTtl, ct);

                return result.Success
                    ? Results.Ok(responseBody)
                    : Results.UnprocessableEntity(responseBody);
            }
            catch (Exception ex)
            {
                await idempotencyStore.FailAsync(key,
                    JsonSerializer.Serialize(new { error = "internal_error", message = ex.Message }),
                    IdempotencyTtl, ct);
                throw;
            }
        })
        .WithName("PayViaBalance");

        // ── POST /api/payments/create ───────────────────────────────────────
        group.MapPost("/create", async (
            CreatePaymentRequest req,
            IMediator mediator,
            IIdempotencyStore idempotencyStore,
            HttpRequest httpReq,
            CancellationToken ct) =>
        {
            if (!httpReq.Headers.TryGetValue("Idempotency-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
                return Results.BadRequest(new { error = "Idempotency-Key header is required." });

            var key = $"create-payment:{rawKey}";

            bool claimed;
            try
            {
                claimed = await idempotencyStore.TryClaimAsync(key, IdempotencyTtl, ct);
            }
            catch (RedisException)
            {
                return Results.StatusCode(503);
            }

            if (!claimed)
            {
                var existing = await idempotencyStore.GetAsync(key, ct);

                if (existing is not null)
                {
                    if (existing.StatusCode == -1)
                        return Results.Json(
                            JsonSerializer.Deserialize<object>(existing.Payload),
                            statusCode: 500);

                    if (existing.StatusCode >= 100)
                        return Results.Json(
                            JsonSerializer.Deserialize<object>(existing.Payload),
                            statusCode: existing.StatusCode);

                    if (existing.CreatedAt < DateTimeOffset.UtcNow - AbandonmentWindow)
                    {
                        await idempotencyStore.DeleteAbandonedAsync(key, ct);

                        try
                        {
                            claimed = await idempotencyStore.TryClaimAsync(key, IdempotencyTtl, ct);
                        }
                        catch (RedisException)
                        {
                            return Results.StatusCode(503);
                        }

                        if (!claimed)
                        {
                            httpReq.HttpContext.Response.Headers.Append("Retry-After", "2");
                            return Results.Json(
                                new { error = "processing", retryAfterSeconds = 2 },
                                statusCode: 409);
                        }
                    }
                    else
                    {
                        httpReq.HttpContext.Response.Headers.Append("Retry-After", "2");
                        return Results.Json(
                            new { error = "processing", retryAfterSeconds = 2 },
                            statusCode: 409);
                    }
                }
            }

            try
            {
                var result = await mediator.Send(new CreatePaymentCommand(
                    req.LicenseId, req.Amount, req.Type,
                    req.ExternalPaymentId, req.ProviderId, req.TargetId), ct);

                var responseBody = new { result.Id };
                await idempotencyStore.CompleteAsync(key, 201,
                    JsonSerializer.Serialize(responseBody), IdempotencyTtl, ct);

                return Results.Created($"/api/payments/{result.Id}", responseBody);
            }
            catch (Exception ex)
            {
                await idempotencyStore.FailAsync(key,
                    JsonSerializer.Serialize(new { error = "internal_error", message = ex.Message }),
                    IdempotencyTtl, ct);
                throw;
            }
        })
        .WithName("CreatePayment");

        // ── POST /api/payments/quick-pay ────────────────────────────────────
        // Check balance → create Pending payment → pay atomically.
        // If balance is insufficient, create a Failed payment and return 422.
        group.MapPost("/quick-pay", async (
            QuickPayRequest req,
            IMediator mediator,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IIdempotencyStore idempotencyStore,
            HttpRequest httpReq,
            CancellationToken ct) =>
        {
            if (!httpReq.Headers.TryGetValue("Idempotency-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
                return Results.BadRequest(new { error = "Idempotency-Key header is required." });

            var key = $"quick-pay:{rawKey}";

            bool claimed;
            try { claimed = await idempotencyStore.TryClaimAsync(key, IdempotencyTtl, ct); }
            catch (RedisException) { return Results.StatusCode(503); }

            if (!claimed)
            {
                var existing = await idempotencyStore.GetAsync(key, ct);
                if (existing is not null)
                {
                    if (existing.StatusCode == -1)
                        return Results.Json(JsonSerializer.Deserialize<object>(existing.Payload), statusCode: 500);
                    if (existing.StatusCode >= 100)
                        return Results.Json(JsonSerializer.Deserialize<object>(existing.Payload), statusCode: existing.StatusCode);
                    if (existing.CreatedAt < DateTimeOffset.UtcNow - AbandonmentWindow)
                    {
                        await idempotencyStore.DeleteAbandonedAsync(key, ct);
                        try { claimed = await idempotencyStore.TryClaimAsync(key, IdempotencyTtl, ct); }
                        catch (RedisException) { return Results.StatusCode(503); }
                        if (!claimed) { httpReq.HttpContext.Response.Headers.Append("Retry-After", "2"); return Results.Json(new { error = "processing", retryAfterSeconds = 2 }, statusCode: 409); }
                    }
                    else { httpReq.HttpContext.Response.Headers.Append("Retry-After", "2"); return Results.Json(new { error = "processing", retryAfterSeconds = 2 }, statusCode: 409); }
                }
            }

            try
            {
                // Step 1 — optimistic balance check (not locked, fast path)
                var balance = await balanceRepo.GetByAccountIdAsync(req.AccountId, ct);
                var available = balance?.Amount ?? 0m;

                if (available < req.Amount)
                {
                    // Create a Failed payment as an audit record of the attempt
                    var failedPayment = new Domain.Entities.Payment
                    {
                        Id = Guid.NewGuid(),
                        LicenseId = req.LicenseId,
                        Amount = req.Amount,
                        Type = req.Type,
                        Status = PaymentStatus.Failed,
                        CreatedAt = DateTime.UtcNow,
                    };
                    await paymentRepo.AddAsync(failedPayment, ct);

                    var failBody = new
                    {
                        success = false,
                        reason = $"Insufficient balance. Required: {req.Amount:F2} GEL, available: {available:F2} GEL.",
                        paymentId = failedPayment.Id,
                    };
                    await idempotencyStore.CompleteAsync(key, 422, JsonSerializer.Serialize(failBody), IdempotencyTtl, ct);
                    return Results.UnprocessableEntity(failBody);
                }

                // Step 2 — create Pending payment
                var createResult = await mediator.Send(
                    new CreatePaymentCommand(req.LicenseId, req.Amount, req.Type, null, null, null), ct);

                // Step 3 — atomic balance debit + mark Paid (handles race on balance)
                var payResult = await paymentRepo.ProcessBalancePaymentAsync(
                    createResult.Id, req.AccountId, req.Amount, ct);

                object responseBody;
                int statusCode;

                if (payResult == ProcessBalancePaymentResult.Success)
                {
                    responseBody = new { success = true, paymentId = createResult.Id };
                    statusCode = 200;
                }
                else
                {
                    // Race: another request drained balance between our check and the debit lock
                    await paymentRepo.MarkFailedAsync(createResult.Id, ct);
                    var updatedBalance = (await balanceRepo.GetByAccountIdAsync(req.AccountId, ct))?.Amount ?? 0m;
                    responseBody = new
                    {
                        success = false,
                        reason = $"Insufficient balance. Required: {req.Amount:F2} GEL, available: {updatedBalance:F2} GEL.",
                        paymentId = createResult.Id,
                    };
                    statusCode = 422;
                }

                await idempotencyStore.CompleteAsync(key, statusCode, JsonSerializer.Serialize(responseBody), IdempotencyTtl, ct);
                return statusCode == 200 ? Results.Ok(responseBody) : Results.UnprocessableEntity(responseBody);
            }
            catch (Exception ex)
            {
                await idempotencyStore.FailAsync(key,
                    JsonSerializer.Serialize(new { error = "internal_error", message = ex.Message }),
                    IdempotencyTtl, ct);
                throw;
            }
        })
        .WithName("QuickPay");

        // ── GET /api/payments/overdue?page=1&pageSize=20 ────────────────────
        group.MapGet("/overdue", async (
            PaymentDbContext db,
            int page,
            int pageSize,
            CancellationToken ct) =>
        {
            var p = page <= 0 ? 1 : page;
            var ps = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);

            var overdue = await db.Payments
                .AsNoTracking()
                .Where(payment => payment.Status == PaymentStatus.Overdue)
                .OrderByDescending(payment => payment.CreatedAt)
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(payment => new
                {
                    payment.Id,
                    payment.LicenseId,
                    payment.Amount,
                    payment.Month,
                    payment.CreatedAt
                })
                .ToListAsync(ct);

            return Results.Ok(overdue);
        })
        .WithName("GetOverduePayments");
    }
}

public record PayViaBalanceRequest(Guid PaymentId, Guid AccountId, decimal Amount);
public record QuickPayRequest(Guid LicenseId, Guid AccountId, PaymentType Type, decimal Amount);
public record CreatePaymentRequest(
    Guid LicenseId,
    decimal Amount,
    PaymentType Type,
    string? ExternalPaymentId = null,
    string? ProviderId = null,
    Guid? TargetId = null);
