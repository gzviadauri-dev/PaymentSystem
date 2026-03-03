using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Interfaces;

namespace Payment.API.Endpoints;

public static class ExternalCallbackEndpoints
{
    public static void MapExternalCallbackEndpoints(this WebApplication app)
    {
        app.MapPost("/api/payments/external/confirm", async (
            HttpRequest httpRequest,
            [FromBody] ExternalPaymentConfirmRequest req,
            IPaymentRepository paymentRepository,
            IConfiguration configuration,
            ILogger<Program> logger) =>
        {
            // ── HMAC-SHA256 webhook signature verification ──────────────────
            // Provider must set: X-Provider-Signature: sha256=<hex>
            // Computed over the raw request body using the shared secret.
            var signature = httpRequest.Headers["X-Provider-Signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(signature))
            {
                logger.LogWarning("External callback rejected: missing X-Provider-Signature header");
                return Results.Unauthorized();
            }

            var secret = configuration["ExternalProviders:WebhookSecret"];
            if (string.IsNullOrEmpty(secret))
            {
                logger.LogError("ExternalProviders:WebhookSecret is not configured");
                return Results.StatusCode(503);
            }

            var body = await ReadRawBodyAsync(httpRequest);
            if (!IsValidSignature(body, signature, secret))
            {
                logger.LogWarning(
                    "External callback rejected: invalid signature for provider {Provider}",
                    req.ProviderId);
                return Results.Unauthorized();
            }
            // ────────────────────────────────────────────────────────────────

            logger.LogInformation(
                "External payment callback accepted: PaymentId={PaymentId}, ExternalId={ExternalId}, Provider={Provider}",
                req.PaymentId, req.ExternalPaymentId, req.ProviderId);

            var result = await paymentRepository.TryConfirmExternalAtomicallyAsync(
                req.PaymentId, req.ExternalPaymentId, req.ProviderId, httpRequest.HttpContext.RequestAborted);

            if (result.WrongProvider)
            {
                // Return 200 to stop the bank retrying, but log as warning.
                // The bank is not at fault — this is a system misconfiguration
                // or a malicious replay. Logging handles the alert.
                logger.LogWarning(
                    "Provider {ProviderId} attempted to confirm payment {PaymentId} " +
                    "which is locked to a different provider. Possible misconfiguration " +
                    "or malicious replay attempt.",
                    req.ProviderId, req.PaymentId);

                return Results.Ok(new
                {
                    processed = false,
                    idempotent = true,
                    reason = "payment_locked_to_other_provider"
                });
            }

            return Results.Ok(new
            {
                processed = true,
                idempotent = result.IsAlreadyProcessed
            });
        })
        .WithName("ExternalPaymentConfirm")
        .WithTags("External")
        .RequireRateLimiting("ExternalCallback");
    }

    private static async Task<byte[]> ReadRawBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        request.Body.Position = 0;
        return ms.ToArray();
    }

    private static bool IsValidSignature(byte[] body, string signatureHeader, string secret)
    {
        // Expected format: "sha256=<hex-digest>"
        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var providedHex = signatureHeader[prefix.Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computedHash = hmac.ComputeHash(body);
        var computedHex = Convert.ToHexString(computedHash);

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(providedHex.ToLowerInvariant()));
    }
}

public record ExternalPaymentConfirmRequest(
    Guid PaymentId,
    string ExternalPaymentId,
    string ProviderId,
    decimal Amount,
    string Currency,
    DateTime PaidAt);
