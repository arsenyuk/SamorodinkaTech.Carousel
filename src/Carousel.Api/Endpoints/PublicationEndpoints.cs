using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Api.Endpoints;

internal static class PublicationEndpoints
{
    public static IEndpointRouteBuilder MapPublicationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/publications", async (
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            Guid? tenantId,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var memberships = await access.GetMembershipsAsync(user.UserId, ct);
            var tenantIds = memberships.Select(m => m.TenantId).ToList();
            if (tenantId is not null)
            {
                if (!tenantIds.Contains(tenantId.Value))
                {
                    throw new ForbiddenException("You are not a member of this workspace.");
                }

                tenantIds = [tenantId.Value];
            }

            var items = await db.Publications.AsNoTracking()
                .Include(p => p.Cards)
                .Include(p => p.Targets)
                .Where(p => tenantIds.Contains(p.TenantId))
                .OrderByDescending(p => p.CreatedAtUtc)
                .ToListAsync(ct);

            return Results.Ok(items.Select(MapPublication).ToList());
        });

        api.MapGet("/publications/{id:guid}", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Viewer, ct);
            return Results.Ok(MapPublication(publication));
        });

        api.MapPost("/publications", async (
            CreatePublicationRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IValidator<CreatePublicationRequest> validator,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            await validator.ValidateAndThrowAsync(request, ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Editor, ct);

            var publication = new Publication
            {
                TenantId = tenantId,
                Title = request.Title,
                Description = request.Description,
            };
            db.Publications.Add(publication);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "PUBLICATION:CREATED",
                TenantId: tenantId,
                UserId: user.UserId,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { publication.Id }), ct);

            return Results.Created($"/api/publications/{publication.Id}", MapPublication(publication));
        });

        api.MapPatch("/publications/{id:guid}", async (
            Guid id,
            UpdatePublicationRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IValidator<UpdatePublicationRequest> validator,
            CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(request, ct);
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            if (request.Title is not null)
            {
                publication.Title = request.Title;
            }

            if (request.Description is not null)
            {
                publication.Description = request.Description;
            }

            if (request.ClearTemplate)
            {
                publication.TemplateId = null;
            }
            else if (request.TemplateId is not null)
            {
                var templateId = request.TemplateId.Value;
                var templateExists = await db.CarouselTemplates
                    .AnyAsync(
                        t => t.Id == templateId
                            && (t.TenantId == null || tenantIds.Contains(t.TenantId.Value)),
                        ct);
                if (!templateExists)
                {
                    throw new NotFoundException("Template not found.");
                }

                publication.TemplateId = request.TemplateId;
            }

            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapPublication(publication));
        });

        api.MapDelete("/publications/{id:guid}", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            db.Publications.Remove(publication);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/publications/{id:guid}/ready", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            if (publication.Cards.Count == 0)
            {
                throw new ForbiddenException("Add at least one card before marking ready.");
            }

            foreach (var card in publication.Cards)
            {
                if (string.IsNullOrWhiteSpace(card.Text) && card.MediaAssetId is null)
                {
                    throw new ForbiddenException("Every card must have text or media.");
                }

                if (card.MediaAssetId is not null)
                {
                    if (card.MediaAsset is null)
                    {
                        throw new ForbiddenException("Card references missing media.");
                    }

                    if (card.MediaAsset.TenantId != publication.TenantId)
                    {
                        throw new ForbiddenException("Media asset does not belong to this tenant.");
                    }
                }
            }

            if (!PublicationStatusRules.CanTransition(publication.Status, PublicationStatus.Ready)
                && publication.Status != PublicationStatus.Ready)
            {
                throw new ForbiddenException($"Cannot mark ready from status {publication.Status}.");
            }

            publication.Status = PublicationStatus.Ready;
            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "PUBLICATION:READY",
                TenantId: publication.TenantId,
                UserId: user.UserId,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { publication.Id }), ct);

            return Results.Ok(new ReadyPublicationResponse(publication.Id, publication.Status.ToString()));
        });

        // --- Cards ---
        api.MapPost("/publications/{id:guid}/cards", async (
            Guid id,
            CreateCardRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IValidator<CreateCardRequest> validator,
            IMediaValidationService media,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            await validator.ValidateAndThrowAsync(request, ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            if (request.MediaAssetId is not null)
            {
                await media.EnsureAssetBelongsToTenantAsync(publication.TenantId, request.MediaAssetId.Value, ct);
            }

            var card = new Card
            {
                PublicationId = publication.Id,
                Order = publication.Cards.Count == 0 ? 0 : publication.Cards.Max(c => c.Order) + 1,
                Text = request.Text,
                MediaAssetId = request.MediaAssetId,
                Styles = ToStyles(request.Styles),
            };
            db.Cards.Add(card);
            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/publications/{id}/cards/{card.Id}", MapCard(card));
        });

        api.MapPatch("/publications/{id:guid}/cards/{cardId:guid}", async (
            Guid id,
            Guid cardId,
            UpdateCardRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IMediaValidationService media,
            IValidator<UpdateCardRequest> validator,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(request, ct);
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            var card = publication.Cards.FirstOrDefault(c => c.Id == cardId)
                ?? throw new NotFoundException("Card not found.");

            if (request.Text is not null)
            {
                card.Text = request.Text;
            }

            if (request.RemoveImage)
            {
                card.MediaAssetId = null;
            }
            else if (request.MediaAssetId is not null)
            {
                await media.EnsureAssetBelongsToTenantAsync(publication.TenantId, request.MediaAssetId.Value, ct);
                card.MediaAssetId = request.MediaAssetId;
            }

            if (request.Order is not null)
            {
                card.Order = request.Order.Value;
            }

            if (request.Styles is not null)
            {
                var styles = request.Styles.Count == 0 ? null : ToStyles(request.Styles);
                var styleErrors = CardStyleRules.Validate(card.Text, styles);
                if (styleErrors.Count > 0)
                {
                    var logger = loggerFactory.CreateLogger("PublicationEndpoints");
                    logger.LogWarning(
                        "Invalid card styles for card {CardId}: {Errors}",
                        card.Id,
                        string.Join("; ", styleErrors));
                    throw new Carousel.Application.Security.ValidationException(string.Join(" ", styleErrors));
                }

                card.Styles = styles;
            }

            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapCard(card));
        });

        // Превью «как уйдёт в Telegram»: байты карточки ровно в publish-виде
        // (фото как есть / рендер PNG 1080×1920) + печать ссылки бота как при postStory.
        api.MapGet("/publications/{id:guid}/cards/{cardId:guid}/story-image", async (
            Guid id,
            Guid cardId,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IStoryImageBuilder storyImageBuilder,
            ITelegramBotApi botApi,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Viewer, ct);

            var card = publication.Cards.FirstOrDefault(c => c.Id == cardId)
                ?? throw new NotFoundException("Card not found.");

            var image = await storyImageBuilder.BuildAsync(publication, card, ct);

            // Печать t.me/… — та же, что выполняет TelegramBotApi перед postStory (если не «без рекламы»).
            if (!publication.NoAds)
            {
                var botUsername = await botApi.GetBotUsernameAsync(ct);
                if (!string.IsNullOrWhiteSpace(botUsername))
                {
                    image = new StoryImageResult(
                        Infrastructure.Publishing.StoryLinkStamp.Stamp(image.Bytes, image.ContentType, $"t.me/{botUsername}"),
                        image.ContentType);
                }
            }

            return Results.File(image.Bytes, image.ContentType);
        });

        api.MapDelete("/publications/{id:guid}/cards/{cardId:guid}", async (
            Guid id,
            Guid cardId,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            var card = publication.Cards.FirstOrDefault(c => c.Id == cardId)
                ?? throw new NotFoundException("Card not found.");

            db.Cards.Remove(card);

            // Правило: пустая карусель = сброс шаблона (единообразно с bulk-clear),
            // когда удаляемая карточка была единственной.
            if (publication.Cards.All(c => c.Id == cardId))
            {
                publication.TemplateId = null;
            }

            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Bulk clear: remove all cards, reset status to Draft so ready/publish can start over.
        api.MapDelete("/publications/{id:guid}/cards", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IAuditService audit,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            var removed = publication.Cards.Count;
            if (removed > 0)
            {
                db.Cards.RemoveRange(publication.Cards);
                publication.Cards.Clear();
            }

            if (publication.Status != PublicationStatus.Draft)
            {
                publication.Status = PublicationStatus.Draft;
            }

            // Очистка = полный сброс оформления: выбранный шаблон тоже снимается.
            publication.TemplateId = null;

            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            loggerFactory.CreateLogger("PublicationEndpoints").LogInformation(
                "Cleared {Count} cards from publication {PublicationId}, template reset",
                removed, publication.Id);

            await audit.WriteAsync(new AuditEntry(
                ActionCode: "PUBLICATION:CARDS_CLEARED",
                TenantId: publication.TenantId,
                UserId: user.UserId,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { publication.Id, Removed = removed }), ct);

            return Results.NoContent();
        });

        api.MapPost("/publications/{id:guid}/cards/reorder", async (
            Guid id,
            ReorderCardsRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IValidator<ReorderCardsRequest> validator,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            await validator.ValidateAndThrowAsync(request, ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            // L7: require exact set match — no unknown IDs, no missing cards.
            if (request.CardIds.Count != publication.Cards.Count ||
                publication.Cards.Any(c => !request.CardIds.Contains(c.Id)))
            {
                throw new Carousel.Application.Security.ValidationException(
                    "Card ids must match the publication's cards exactly.");
            }

            for (var i = 0; i < request.CardIds.Count; i++)
            {
                var card = publication.Cards.FirstOrDefault(c => c.Id == request.CardIds[i]);
                if (card is not null)
                {
                    card.Order = i;
                }
            }

            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapPublication(publication));
        });

        // --- Publish ---
        // «Другие площадки» (generic publish по accountIds и publish/chat) отключены —
        // будет отдельной функциональностью. Код сохранён в git history.
        // User-story: publish carousel into the current user's Telegram DM (no manual account connect).
        api.MapPost("/publications/{id:guid}/publish/me", async (
            Guid id,
            PublishMeRequest? request,
            ICurrentUserContext context,
            ITenantAccessService access,
            IPublishCoordinator coordinator,
            IPublicationStarsPaymentService stars,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            if (publication.Status == PublicationStatus.Draft)
            {
                throw new Carousel.Application.Security.ValidationException(
                    "Publication is still Draft. Call POST /api/publications/{id}/ready first.");
            }

            // Монетизация: платим если есть ФОТО или включён крыжек «без рекламы»
            // (цена при этом одна — StarsPrice, не суммируется).
            var hasPhoto = publication.Cards.Any(c => c.MediaAssetId != null);
            var noAds = request?.NoAds == true;
            if (publication.NoAds != noAds)
            {
                publication.NoAds = noAds;
                publication.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            var gate = await stars.GetGateAsync(
                publication.TenantId, id, user.TelegramUsername, publication.Title,
                hasPhoto || noAds, ct);
            if (gate.State == StarsPaymentState.Pending)
            {
                return Results.Ok(new PublishResponse(
                    id, "PaymentRequired", Array.Empty<DeliveryOutcomeDto>(), gate.InvoiceUrl));
            }

            var externalId = user.TelegramUserId.ToString();
            var account = await db.PlatformAccounts.FirstOrDefaultAsync(
                a => a.TenantId == publication.TenantId
                    && a.Platform == Platform.Telegram
                    && a.ExternalId == externalId,
                ct);

            if (account is null)
            {
                account = new PlatformAccount
                {
                    TenantId = publication.TenantId,
                    Platform = Platform.Telegram,
                    ExternalId = externalId,
                    DisplayName = user.TelegramUsername ?? $"@{user.TelegramUserId}",
                };
                db.PlatformAccounts.Add(account);
                await db.SaveChangesAsync(ct);
            }

            var result = await coordinator.PublishAsync(
                id, [account.Id], ct, skipQuota: gate.State == StarsPaymentState.Paid);
            if (result.Status != "Published")
            {
                var error = result.Deliveries.FirstOrDefault(d => d.Status != "Succeeded")?.Error
                    ?? "Публикация не удалась.";
                // Фундаментальные причины (повтор не поможет) → возврат звёзд.
                var refunded = await stars.TryRefundOnFailureAsync(user.TelegramUserId, id, error, ct);
                throw new ConflictException(
                    refunded ? $"{error} Звёзды возвращены." : error);
            }

            return Results.Ok(ToPublishResponse(result));
        });

        api.MapPost("/publications/{id:guid}/publish/retry", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            IPublishCoordinator coordinator,
            IPublicationStarsPaymentService stars,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var publication = await EndpointHelpers.FindPublicationAsync(db, tenantIds, id, ct);
            await EndpointHelpers.EnsurePublicationAccessAsync(access, user.UserId, publication.TenantId, MemberRole.Editor, ct);

            // После refund повтор требует НОВОЙ оплаты; при Completed-оплате — skipQuota.
            var hasPhoto = publication.Cards.Any(c => c.MediaAssetId != null);
            var gate = await stars.GetGateAsync(
                publication.TenantId, id, user.TelegramUsername, publication.Title,
                hasPhoto || publication.NoAds, ct);
            if (gate.State == StarsPaymentState.Pending)
            {
                return Results.Ok(new PublishResponse(
                    id, "PaymentRequired", Array.Empty<DeliveryOutcomeDto>(), gate.InvoiceUrl));
            }

            var result = await coordinator.RetryAsync(id, ct, skipQuota: gate.State == StarsPaymentState.Paid);
            EnsurePublished(result);
            return Results.Ok(ToPublishResponse(result));
        });

        return app;
    }

    /// <summary>
    /// Delivery failures become HTTP 409 with the exact error text so the Mini App
    /// dialog shows "POST ... → 409: Ошибка публикации. ...".
    /// </summary>
    private static void EnsurePublished(PublishCoordinatorResult result)
    {
        if (result.Status == "Published")
        {
            return;
        }

        var error = result.Deliveries.FirstOrDefault(d => d.Status != "Succeeded")?.Error
            ?? "Публикация не удалась.";
        throw new ConflictException(error);
    }

    private static PublishResponse ToPublishResponse(PublishCoordinatorResult result) => new(
        result.PublicationId,
        result.Status,
        result.Deliveries.Select(d => new DeliveryOutcomeDto(d.TargetId, d.Platform, d.Status, d.ExternalPostId, d.Error)).ToList());

    private static PublicationDto MapPublication(Publication p) => new(
        p.Id,
        p.TenantId,
        p.Title,
        p.Description,
        p.Status.ToString(),
        p.TemplateId,
        p.CreatedAtUtc,
        p.UpdatedAtUtc,
        p.Cards.OrderBy(c => c.Order).Select(MapCard).ToList(),
        p.Targets.Select(t => new PublicationTargetDto(t.Id, t.Platform.ToString(), t.PlatformAccountId, t.Status.ToString())).ToList(),
        p.NoAds);

    private static CardDto MapCard(Card c) => new(c.Id, c.Order, c.Text, c.MediaAssetId, MapStyles(c.Styles));

    private static IReadOnlyList<CardStyleDto>? MapStyles(CardStyle[]? styles) =>
        styles?.Select(s => new CardStyleDto(s.FontName, s.Color, s.FontSize, s.FontStyle)).ToList();

    private static CardStyle[]? ToStyles(IReadOnlyList<CardStyleDto>? styles) =>
        styles?.Select(s => new CardStyle
        {
            FontName = s.FontName,
            Color = s.Color,
            FontSize = s.FontSize,
            FontStyle = s.FontStyle,
        }).ToArray();
}
