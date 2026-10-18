namespace Carousel.Application.Abstractions;

/// <summary>Итог одного прогона retention-очистки.</summary>
public sealed record CleanupReport(int PublicationsRemoved, int MediaAssetsRemoved);

/// <summary>
/// Retention-очистка БД: публикации старше срока (каскадом) и осиротевшие media_assets.
/// Вызывается фоновым джобом (см. DataCleanupHostedService); логика отделена от хостинга
/// ради тестируемости.
/// </summary>
public interface IDataCleanupService
{
    Task<CleanupReport> RunOnceAsync(CancellationToken ct = default);
}