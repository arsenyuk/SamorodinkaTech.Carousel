using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public interface IInstagramPublisher
{
    Task<PublishResult> PublishCarouselAsync(InstagramPublishRequest request, CancellationToken ct = default);
}

public sealed record InstagramPublishRequest(
    Guid PublicationId,
    IReadOnlyList<PublishCard> Cards,
    string AccountExternalId);
