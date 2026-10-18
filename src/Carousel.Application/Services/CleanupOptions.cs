namespace Carousel.Application.Services;

/// <summary>
/// Retention-очистка БД (фоновый джоб): публикации старше N дней (каскадом карточки/доставки)
/// и осиротевшие media_assets старше M дней. Payments/billing_events и audit_events не трогаются.
/// </summary>
public sealed record CleanupOptions(
    bool Enabled,
    int PublicationRetentionDays,
    int OrphanMediaRetentionDays,
    int IntervalHours)
{
    /// <summary>Разбирает и проверяет конфигурацию Cleanup. Бросает InvalidOperationException при невалидности.</summary>
    public static CleanupOptions Validate(
        bool enabled,
        string? publicationRetentionDaysRaw,
        string? orphanMediaRetentionDaysRaw,
        string? intervalHoursRaw)
    {
        if (!int.TryParse(publicationRetentionDaysRaw, out var pubDays) || pubDays < 1)
        {
            throw new InvalidOperationException(
                $"Cleanup:PublicationRetentionDays must be an integer >= 1, got '{publicationRetentionDaysRaw ?? "null"}'.");
        }

        if (!int.TryParse(orphanMediaRetentionDaysRaw, out var mediaDays) || mediaDays < 1)
        {
            throw new InvalidOperationException(
                $"Cleanup:OrphanMediaRetentionDays must be an integer >= 1, got '{orphanMediaRetentionDaysRaw ?? "null"}'.");
        }

        if (!int.TryParse(intervalHoursRaw, out var interval) || interval < 1)
        {
            throw new InvalidOperationException(
                $"Cleanup:IntervalHours must be an integer >= 1, got '{intervalHoursRaw ?? "null"}'.");
        }

        return new CleanupOptions(enabled, pubDays, mediaDays, interval);
    }
}