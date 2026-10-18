using System.Text.Json;
using System.Text.RegularExpressions;
using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ValidationException = Carousel.Application.Security.ValidationException;

namespace Carousel.Api.Endpoints;

internal static partial class TemplateEndpoints
{
    [GeneratedRegex("^[a-z0-9-]{1,50}$")]
    private static partial Regex CodeRegex();

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/templates", async (
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            var items = await db.CarouselTemplates.AsNoTracking()
                .Where(t => t.TenantId == null || t.TenantId == tenantId)
                .OrderBy(t => t.TenantId != null) // seed (null) first
                .ThenBy(t => t.CreatedAtUtc)
                .ToListAsync(ct);

            return Results.Ok(items.Select(t => MapTemplate(t, tenantId)).ToList());
        });

        api.MapGet("/templates/{id:guid}", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            var template = await FindVisibleTemplateAsync(db, tenantId, id, ct);
            return Results.Ok(MapTemplate(template, tenantId));
        });

        api.MapGet("/templates/{id:guid}/background", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IMediaStorage storage,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            var template = await FindVisibleTemplateAsync(db, tenantId, id, ct);

            if (template.BackgroundMediaAssetId is null)
            {
                throw new NotFoundException("Template has no background.");
            }

            var content = await storage.GetAsync(template.BackgroundMediaAssetId.Value, ct)
                ?? throw new NotFoundException("Template background media not found.");

            return Results.File(content.Content, content.ContentType);
        });

        api.MapPost("/templates", async (
            CreateTemplateRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IMediaStorage storage,
            IMediaValidationService mediaValidation,
            ITemplateBackgroundRenderer renderer,
            IValidator<CreateTemplateRequest> validator,
            ILogger<Endpoint> logger,
            CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(request, ct);
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Editor, ct);

            var code = await ResolveCreateCodeAsync(db, tenantId, request.Name, request.Code, ct);
            var backgroundMediaAssetId = await ResolveBackgroundAsync(
                tenantId,
                request.BackgroundMediaAssetId,
                request.GradientFromHex,
                request.GradientToHex,
                storage,
                mediaValidation,
                renderer,
                logger,
                ct);

            var template = new CarouselTemplate
            {
                TenantId = tenantId,
                Code = code,
                Name = request.Name,
                Description = request.Description,
                DefaultStylesJson = SerializeStyles(request.DefaultStyles),
                BackgroundMediaAssetId = backgroundMediaAssetId,
            };

            db.CarouselTemplates.Add(template);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/templates/{template.Id}", MapTemplate(template, tenantId));
        });

        api.MapPut("/templates/{id:guid}", async (
            Guid id,
            UpdateTemplateRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IMediaStorage storage,
            IMediaValidationService mediaValidation,
            ITemplateBackgroundRenderer renderer,
            IValidator<UpdateTemplateRequest> validator,
            ILogger<Endpoint> logger,
            CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(request, ct);
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Editor, ct);

            var template = await db.CarouselTemplates
                .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct)
                ?? throw new NotFoundException("Template not found.");

            if (request.Name is not null)
            {
                template.Name = request.Name;
            }

            if (request.Description is not null)
            {
                template.Description = request.Description;
            }

            if (request.DefaultStyles is not null)
            {
                template.DefaultStylesJson = SerializeStyles(request.DefaultStyles);
            }

            var hasBackgroundChange = request.BackgroundMediaAssetId is not null
                || !string.IsNullOrWhiteSpace(request.GradientFromHex)
                || !string.IsNullOrWhiteSpace(request.GradientToHex);
            if (hasBackgroundChange)
            {
                template.BackgroundMediaAssetId = await ResolveBackgroundAsync(
                    tenantId,
                    request.BackgroundMediaAssetId,
                    request.GradientFromHex,
                    request.GradientToHex,
                    storage,
                    mediaValidation,
                    renderer,
                    logger,
                    ct);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(MapTemplate(template, tenantId));
        });

        api.MapDelete("/templates/{id:guid}", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Editor, ct);

            var template = await db.CarouselTemplates
                .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct)
                ?? throw new NotFoundException("Template not found.");

            db.CarouselTemplates.Remove(template);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<CarouselTemplate> FindVisibleTemplateAsync(
        CarouselDbContext db,
        Guid tenantId,
        Guid id,
        CancellationToken ct)
    {
        return await db.CarouselTemplates.AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.Id == id && (t.TenantId == null || t.TenantId == tenantId),
                ct)
            ?? throw new NotFoundException("Template not found.");
    }

    private static async Task<string> ResolveCreateCodeAsync(
        CarouselDbContext db,
        Guid tenantId,
        string name,
        string? explicitCode,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(explicitCode))
        {
            if (!CodeRegex().IsMatch(explicitCode))
            {
                throw new ValidationException("Code must match ^[a-z0-9-]{1,50}$.");
            }

            var exists = await db.CarouselTemplates.AnyAsync(
                t => t.TenantId == tenantId && t.Code == explicitCode,
                ct);
            if (exists)
            {
                throw new ValidationException($"Template code '{explicitCode}' already exists.");
            }

            return explicitCode;
        }

        var baseCode = Slugify(name);
        if (string.IsNullOrEmpty(baseCode))
        {
            baseCode = "template";
        }

        var candidate = baseCode;
        var suffix = 2;
        while (await db.CarouselTemplates.AnyAsync(t => t.TenantId == tenantId && t.Code == candidate, ct))
        {
            var suffixText = suffix.ToString();
            var maxBaseLength = 50 - suffixText.Length - 1;
            var truncated = baseCode.Length > maxBaseLength ? baseCode[..maxBaseLength].TrimEnd('-') : baseCode;
            candidate = $"{truncated}-{suffixText}";
            suffix++;
        }

        return candidate;
    }

    private static async Task<Guid?> ResolveBackgroundAsync(
        Guid tenantId,
        Guid? backgroundMediaAssetId,
        string? gradientFromHex,
        string? gradientToHex,
        IMediaStorage storage,
        IMediaValidationService mediaValidation,
        ITemplateBackgroundRenderer renderer,
        ILogger logger,
        CancellationToken ct)
    {
        if (backgroundMediaAssetId is not null)
        {
            await mediaValidation.EnsureAssetBelongsToTenantAsync(tenantId, backgroundMediaAssetId.Value, ct);
            return backgroundMediaAssetId;
        }

        if (!string.IsNullOrWhiteSpace(gradientFromHex) && !string.IsNullOrWhiteSpace(gradientToHex))
        {
            var png = renderer.GenerateVerticalGradientPng(gradientFromHex, gradientToHex);
            var saved = await storage.SaveAsync(tenantId, "image/png", png, 1080, 1080, ct);
            return saved.MediaAssetId;
        }

        logger.LogWarning(
            "Background missing for template in tenant {TenantId}: mediaId={MediaId}, from={From}, to={To}",
            tenantId,
            backgroundMediaAssetId,
            gradientFromHex,
            gradientToHex);
        throw new ValidationException("Background is required: provide BackgroundMediaAssetId or both GradientFromHex and GradientToHex.");
    }

    private static string Slugify(string name)
    {
        // Codes must match ^[a-z0-9-]{1,50}$; non-ASCII letters (e.g. Cyrillic) collapse to '-'.
        var chars = name.ToLowerInvariant()
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    private static string? SerializeStyles(IReadOnlyList<CardStyleDto>? styles)
    {
        if (styles is null)
        {
            return null;
        }

        var entities = styles
            .Select(s => new CardStyle
            {
                FontName = s.FontName,
                Color = s.Color,
                FontSize = s.FontSize,
                FontStyle = s.FontStyle,
            })
            .ToList();

        return JsonSerializer.Serialize(entities);
    }

    private static TemplateDto MapTemplate(CarouselTemplate t, Guid currentTenantId) => new(
        t.Id,
        t.Code,
        t.Name,
        t.Description,
        t.BackgroundMediaAssetId,
        ParseStyles(t.DefaultStylesJson),
        t.TenantId,
        t.TenantId == currentTenantId);

    private static IReadOnlyList<CardStyleDto>? ParseStyles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var styles = JsonSerializer.Deserialize<List<CardStyle>>(json);
            return styles?
                .Select(s => new CardStyleDto(s.FontName, s.Color, s.FontSize, s.FontStyle))
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
