using Carousel.Infrastructure.Instagram;
using Microsoft.Extensions.Logging;

namespace Carousel.Api.Endpoints;

/// <summary>
/// Development-only endpoints to inspect the Instagram mock outbox.
/// Requires InstagramMockOutbox in DI (Instagram:UseMock).
/// </summary>
internal static class InstagramMockEndpoints
{
    public static IEndpointRouteBuilder MapInstagramMockEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/dev/instagram");

        api.MapGet("/outbox", (InstagramMockOutbox outbox) =>
        {
            var posts = outbox.Peek().Select(p => new
            {
                accountExternalId = p.AccountExternalId,
                publicationId = p.PublicationId,
                caption = p.Caption,
                externalPostId = p.ExternalPostId,
                atUtc = p.AtUtc,
                slides = p.Slides.Select(s => new
                {
                    mediaAssetId = s.MediaAssetId,
                    text = s.Text,
                    imageBytes = s.ImageBytes,
                }),
            });
            return Results.Ok(posts);
        });

        api.MapDelete("/outbox", (InstagramMockOutbox outbox) =>
        {
            outbox.Drain();
            return Results.NoContent();
        });

        // Arm forced failure of the next mock publish (retry / PartiallyFailed tests).
        api.MapPost("/fail-next", (InstagramMockOutbox outbox, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("InstagramMockEndpoints");
            outbox.ArmFailNext();
            logger.LogInformation("Instagram mock armed to fail the next publish.");
            return Results.Accepted();
        });

        return app;
    }
}
