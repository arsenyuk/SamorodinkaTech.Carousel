using Carousel.Application.Abstractions;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Publishing;

public sealed class PublishCoordinator : IPublishCoordinator
{
    private readonly CarouselDbContext _db;
    private readonly ITelegramPublisher _telegram;
    private readonly IInstagramPublisher _instagram;
    private readonly ILimitService _limits;
    private readonly IStoryImageBuilder _storyImageBuilder;
    private readonly ILogger<PublishCoordinator> _logger;

    public PublishCoordinator(
        CarouselDbContext db,
        ITelegramPublisher telegram,
        IInstagramPublisher instagram,
        ILimitService limits,
        IStoryImageBuilder storyImageBuilder,
        ILogger<PublishCoordinator> logger)
    {
        _db = db;
        _telegram = telegram;
        _instagram = instagram;
        _limits = limits;
        _storyImageBuilder = storyImageBuilder;
        _logger = logger;
    }

    public async Task<PublishCoordinatorResult> PublishAsync(
        Guid publicationId, IReadOnlyList<Guid> targetAccountIds, CancellationToken ct = default, bool skipQuota = false)
    {
        var publication = await _db.Publications
            .Include(p => p.Cards).ThenInclude(c => c.MediaAsset)
            .Include(p => p.Targets).ThenInclude(t => t.Deliveries)
            .FirstOrDefaultAsync(p => p.Id == publicationId, ct)
            ?? throw new NotFoundException($"Publication {publicationId} not found.");

        EnsureContentReady(publication);

        // Stage 2: explicit ready gate — Draft must go through POST /ready first.
        if (publication.Status == PublicationStatus.Draft)
        {
            throw new Carousel.Application.Security.ValidationException(
                "Publication is still Draft. Call POST /api/publications/{id}/ready first.");
        }

        var accounts = await _db.PlatformAccounts
            .Where(a => targetAccountIds.Contains(a.Id) && a.TenantId == publication.TenantId && a.Status == PlatformAccountStatus.Active)
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            throw new ForbiddenException("No active platform accounts for this tenant.");
        }

        if (!skipQuota)
        {
            await _limits.EnsureCanPublishAsync(publication.TenantId, publication.Cards.Count, ct);

            // H3: atomically reserve quota BEFORE delivery to prevent TOCTOU race.
            // Two concurrent publishes cannot both pass used < max anymore.
            await _limits.ReservePublicationAsync(publication.TenantId, ct);
        }

        foreach (var existing in publication.Targets.ToList())
        {
            if (!targetAccountIds.Contains(existing.PlatformAccountId))
            {
                _db.PublicationTargets.Remove(existing);
            }
        }

        await _db.SaveChangesAsync(ct);

        foreach (var account in accounts)
        {
            var target = publication.Targets.FirstOrDefault(t => t.PlatformAccountId == account.Id);
            if (target is null)
            {
                target = new PublicationTarget
                {
                    PublicationId = publication.Id,
                    Platform = account.Platform,
                    PlatformAccountId = account.Id,
                    Status = DeliveryStatus.Pending,
                };
                publication.Targets.Add(target);
                _db.PublicationTargets.Add(target);
            }
            else if (target.Status is DeliveryStatus.Succeeded)
            {
                continue;
            }
            else
            {
                target.Status = DeliveryStatus.Pending;
            }
        }

        if (!PublicationStatusRules.CanTransition(publication.Status, PublicationStatus.Publishing))
        {
            // Allow PartiallyFailed/Failed/Draft(Ready) → Publishing
            if (publication.Status is not (PublicationStatus.Ready or PublicationStatus.PartiallyFailed or PublicationStatus.Failed))
            {
                throw new ForbiddenException($"Cannot publish from status {publication.Status}.");
            }
        }

        publication.Status = PublicationStatus.Publishing;
        publication.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await DeliverAsync(publication, ct);

        // H3 compensation: release reserved slot if ALL deliveries failed
        // (only when a slot was actually reserved — skipQuota reserves nothing).
        if (!skipQuota && !publication.Targets.Any(t => t.Status == DeliveryStatus.Succeeded))
        {
            await _limits.ReleasePublicationAsync(publication.TenantId, ct);
        }

        return await BuildResultAsync(publication.Id, ct);
    }

    public async Task<PublishCoordinatorResult> RetryAsync(
        Guid publicationId, CancellationToken ct = default, bool skipQuota = false)
    {
        var publication = await _db.Publications
            .Include(p => p.Cards).ThenInclude(c => c.MediaAsset)
            .Include(p => p.Targets).ThenInclude(t => t.Deliveries)
            .FirstOrDefaultAsync(p => p.Id == publicationId, ct)
            ?? throw new NotFoundException($"Publication {publicationId} not found.");

        EnsureContentReady(publication);

        if (publication.Targets.All(t => t.Status is not (DeliveryStatus.Failed or DeliveryStatus.Pending or DeliveryStatus.InProgress)))
        {
            return await BuildResultAsync(publication.Id, ct);
        }

        if (!skipQuota)
        {
            // H4: same checks as PublishAsync — subscription + quota reserve.
            // Prevents free re-publish after limit exhaustion or subscription expiry.
            await _limits.EnsureCanPublishAsync(publication.TenantId, publication.Cards.Count, ct);
            await _limits.ReservePublicationAsync(publication.TenantId, ct);
        }

        publication.Status = PublicationStatus.Publishing;
        await _db.SaveChangesAsync(ct);
        await DeliverAsync(publication, ct, onlyUnsucceeded: true);

        // H4 compensation: release reserved slot if ALL (remaining) deliveries failed.
        if (!skipQuota && !publication.Targets.Any(t => t.Status == DeliveryStatus.Succeeded))
        {
            await _limits.ReleasePublicationAsync(publication.TenantId, ct);
        }

        return await BuildResultAsync(publication.Id, ct);
    }

    private static void EnsureContentReady(Publication publication)
    {
        if (publication.Cards.Count == 0)
        {
            throw new ForbiddenException("Publication must have at least one card.");
        }

        foreach (var card in publication.Cards)
        {
            if (string.IsNullOrWhiteSpace(card.Text) && card.MediaAssetId is null)
            {
                throw new ForbiddenException("Every card must have text or media.");
            }

            if (card.MediaAsset is not null && card.MediaAsset.TenantId != publication.TenantId)
            {
                throw new ForbiddenException("Media asset does not belong to this tenant.");
            }
        }
    }

    private async Task DeliverAsync(Publication publication, CancellationToken ct, bool onlyUnsucceeded = false)
    {
        foreach (var target in publication.Targets)
        {
            if (onlyUnsucceeded && target.Status is DeliveryStatus.Succeeded)
            {
                continue;
            }

            if (target.Status is DeliveryStatus.Succeeded)
            {
                continue;
            }

            var attempt = (target.Deliveries.Count == 0 ? 0 : target.Deliveries.Max(d => d.Attempt)) + 1;
            var delivery = new PublicationDelivery
            {
                PublicationTargetId = target.Id,
                Attempt = attempt,
                Status = DeliveryStatus.InProgress,
                StartedAtUtc = DateTime.UtcNow,
            };
            _db.PublicationDeliveries.Add(delivery);
            target.Status = DeliveryStatus.InProgress;
            await _db.SaveChangesAsync(ct);

            try
            {
                var account = await _db.PlatformAccounts.FirstAsync(a => a.Id == target.PlatformAccountId, ct);

                var cards = new List<PublishCard>();
                foreach (var c in publication.Cards.OrderBy(c => c.Order))
                {
                    // Единый источник байтов (фото или рендер PNG 1080×1920) —
                    // тот же код, что в режиме «Превью».
                    var image = await _storyImageBuilder.BuildAsync(publication, c, ct);
                    cards.Add(new PublishCard(c.Text, image.Bytes, image.ContentType, c.MediaAssetId));
                }

                PublishResult result = account.Platform switch
                {
                    Platform.Telegram => await _telegram.PublishCarouselAsync(
                        new TelegramPublishRequest(publication.Id, publication.Title, publication.Description, cards, account.ExternalId, publication.NoAds),
                        ct),
                    Platform.Instagram => await _instagram.PublishCarouselAsync(
                        new InstagramPublishRequest(publication.Id, cards, account.ExternalId),
                        ct),
                    _ => new PublishResult(false, null, "Unsupported platform."),
                };

                delivery.Status = result.Success ? DeliveryStatus.Succeeded : DeliveryStatus.Failed;
                delivery.ExternalPostId = result.ExternalPostId;
                delivery.Error = result.Error;
                target.Status = delivery.Status;
            }
            catch (Exception ex) when (ex is not ForbiddenException)
            {
                _logger.LogWarning(ex,
                    "Delivery failed for publication {PublicationId} target {TargetId}: {Message}",
                    publication.Id, target.Id, ex.Message);
                delivery.Status = DeliveryStatus.Failed;
                delivery.Error = ex.Message;
                target.Status = DeliveryStatus.Failed;
            }
            finally
            {
                delivery.FinishedAtUtc = DateTime.UtcNow;
            }
        }

        var statuses = publication.Targets.Select(t => t.Status).ToList();
        publication.Status = statuses.All(s => s == DeliveryStatus.Succeeded)
            ? PublicationStatus.Published
            : statuses.Any(s => s == DeliveryStatus.Succeeded)
                ? PublicationStatus.PartiallyFailed
                : PublicationStatus.Failed;
        publication.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<PublishCoordinatorResult> BuildResultAsync(Guid publicationId, CancellationToken ct)
    {
        var publication = await _db.Publications.AsNoTracking()
            .Include(p => p.Targets)
            .ThenInclude(t => t.Deliveries)
            .FirstAsync(p => p.Id == publicationId, ct);

        var deliveries = publication.Targets
            .Select(t =>
            {
                var last = t.Deliveries.OrderByDescending(d => d.Attempt).FirstOrDefault();
                return new DeliveryOutcome(t.Id, t.Platform.ToString(), t.Status.ToString(), last?.ExternalPostId, last?.Error);
            })
            .ToList();

        return new PublishCoordinatorResult(publication.Id, publication.Status.ToString(), deliveries);
    }
}
