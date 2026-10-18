namespace Carousel.Application.Abstractions;

public interface IPublishCoordinator
{
    /// <param name="skipQuota">
    /// true = публикация оплачена Stars: не проверять и не резервировать квоту/подписку.
    /// </param>
    Task<PublishCoordinatorResult> PublishAsync(
        Guid publicationId, IReadOnlyList<Guid> targetAccountIds,
        CancellationToken ct = default, bool skipQuota = false);

    Task<PublishCoordinatorResult> RetryAsync(
        Guid publicationId, CancellationToken ct = default, bool skipQuota = false);
}

public sealed record PublishCoordinatorResult(
    Guid PublicationId,
    string Status,
    IReadOnlyList<DeliveryOutcome> Deliveries);

public sealed record DeliveryOutcome(Guid TargetId, string Platform, string Status, string? ExternalPostId, string? Error);
