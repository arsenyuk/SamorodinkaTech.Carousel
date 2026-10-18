using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Microsoft.Extensions.Logging;

namespace Carousel.Api.Endpoints;

/// <summary>
/// Development-only: имитация успешной оплаты Stars (dev без Telegram и интеграционные тесты).
/// </summary>
internal static class BillingMockEndpoints
{
    public static IEndpointRouteBuilder MapBillingMockEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/billing/stars-paid", async (
            StarsPaidRequest request,
            IPublicationStarsPaymentService stars,
            StarsBillingOptions options,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("BillingMockEndpoints");
            if (request.PublicationId == Guid.Empty)
            {
                logger.LogWarning(
                    "Validation failed for {Path}: {Reason}",
                    "/api/dev/billing/stars-paid", "publicationId is required");
                return Results.BadRequest(new { error = "publicationId is required" });
            }

            var ok = await stars.CompleteAsync(
                $"pub:{request.PublicationId}",
                $"mock-{Guid.NewGuid():N}",
                options.StarsPrice,
                ct);
            if (!ok)
            {
                logger.LogWarning(
                    "stars-paid for unknown pending payment {PublicationId} — сначала publish/me",
                    request.PublicationId);
                return Results.NotFound(new { error = "No pending payment for this publication. Call publish/me first." });
            }

            return Results.Ok(new { chargeId = $"mock-{Guid.NewGuid():N}" });
        });

        return app;
    }
}

internal sealed record StarsPaidRequest(Guid PublicationId);