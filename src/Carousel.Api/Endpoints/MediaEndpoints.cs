using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Api.Endpoints;

internal static class MediaEndpoints
{
    // L5: per-endpoint size cap — Kestrel global limit is 20 MB, but media should be tighter.
    private const long MaxUploadBytes = 12 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/media", async (
            HttpContext ctx,
            ICurrentUserContext context,
            ITenantAccessService access,
            IMediaValidationService media,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            // M1 fix: require Editor role — Viewer must not upload media (storage/quota abuse).
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Editor, ct);

            if (!ctx.Request.HasFormContentType)
            {
                throw new Carousel.Application.Security.ValidationException("multipart/form-data required.");
            }

            var form = await ctx.Request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                throw new Carousel.Application.Security.ValidationException("file required.");
            }

            // L5: reject oversized files before buffering into memory.
            if (file.Length > MaxUploadBytes)
            {
                throw new Carousel.Application.Security.ValidationException(
                    $"File size {file.Length} bytes exceeds limit {MaxUploadBytes / 1024 / 1024} MB.");
            }

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            var result = await media.ValidateAndSaveAsync(tenantId, contentType, bytes, ct);

            return Results.Created($"/api/media/{result.MediaAssetId}", new MediaAssetDto(
                result.MediaAssetId,
                result.ContentType,
                result.SizeBytes,
                result.Width,
                result.Height));
        }).RequireRateLimiting("upload");

        api.MapGet("/media/{id:guid}", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IMediaStorage storage,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var asset = await db.MediaAssets.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id && tenantIds.Contains(m.TenantId), ct);

            if (asset is null)
            {
                throw new NotFoundException("Media not found.");
            }

            var content = await storage.GetAsync(id, ct);
            if (content is null)
            {
                throw new NotFoundException("Media content not found.");
            }

            return Results.File(content.Content, content.ContentType);
        });

        return app;
    }
}
