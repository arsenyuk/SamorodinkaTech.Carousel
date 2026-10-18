using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public interface ITelegramPublisher
{
    Task<PublishResult> PublishCarouselAsync(TelegramPublishRequest request, CancellationToken ct = default);
}

public sealed record TelegramPublishRequest(
    Guid PublicationId,
    string Title,
    string? Description,
    IReadOnlyList<PublishCard> Cards,
    string ChannelExternalId,
    bool NoAds = false);

public sealed record PublishCard(
    string? Text,
    byte[]? Image,
    string? ContentType,
    Guid? MediaAssetId = null);

public sealed record PublishResult(bool Success, string? ExternalPostId, string? Error);
