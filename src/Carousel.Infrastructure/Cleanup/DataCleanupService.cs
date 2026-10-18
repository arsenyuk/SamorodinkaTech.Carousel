using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Cleanup;

/// <summary>
/// Один прогон retention-очистки (см. <see cref="IDataCleanupService"/>).
/// Порядок: сначала старые публикации (cards/targets/deliveries — каскад в БД),
/// затем осиротевшие media_assets — в том же прогоне подхватываются осиротевшие от этих публикаций.
/// Payments/billing_events не затрагиваются (FK к публикации нет), audit_events не трогаем.
/// </summary>
public sealed class DataCleanupService : IDataCleanupService
{
    private readonly CarouselDbContext _db;
    private readonly CleanupOptions _options;
    private readonly ILogger<DataCleanupService> _logger;

    public DataCleanupService(CarouselDbContext db, CleanupOptions options, ILogger<DataCleanupService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task<CleanupReport> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new CleanupReport(0, 0);
        }

        var now = DateTime.UtcNow;
        var publicationCutoff = now.AddDays(-_options.PublicationRetentionDays);
        var mediaCutoff = now.AddDays(-_options.OrphanMediaRetentionDays);

        var publicationsRemoved = 0;
        var mediaRemoved = 0;

        try
        {
            // Все статусы, кроме Publishing (публикация в полёте) — по возрасту.
            var stalePublications = await _db.Publications
                .Where(p => p.CreatedAtUtc < publicationCutoff
                    && p.Status != PublicationStatus.Publishing)
                .ToListAsync(ct);
            if (stalePublications.Count > 0)
            {
                _db.Publications.RemoveRange(stalePublications);
                publicationsRemoved = stalePublications.Count;
                await _db.SaveChangesAsync(ct);
            }

            // Осиротевшие картинки: нет ссылок из карточек и фонов шаблонов, возраст > срока.
            var orphanMedia = await _db.MediaAssets
                .Where(m => m.CreatedAtUtc < mediaCutoff
                    && !_db.Cards.Any(c => c.MediaAssetId == m.Id)
                    && !_db.CarouselTemplates.Any(t => t.BackgroundMediaAssetId == m.Id))
                .ToListAsync(ct);
            if (orphanMedia.Count > 0)
            {
                _db.MediaAssets.RemoveRange(orphanMedia);
                mediaRemoved = orphanMedia.Count;
                await _db.SaveChangesAsync(ct);
            }

            if (publicationsRemoved > 0 || mediaRemoved > 0)
            {
                _logger.LogInformation(
                    "Cleanup: удалено публикаций {Pubs}, осиротевших медиа {Media} (retention {PubDays}д / {MediaDays}д)",
                    publicationsRemoved, mediaRemoved,
                    _options.PublicationRetentionDays, _options.OrphanMediaRetentionDays);
            }

            return new CleanupReport(publicationsRemoved, mediaRemoved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup: прогон очистки прерван (удалено публикаций {Pubs}, медиа {Media})",
                publicationsRemoved, mediaRemoved);
            return new CleanupReport(publicationsRemoved, mediaRemoved);
        }
    }
}