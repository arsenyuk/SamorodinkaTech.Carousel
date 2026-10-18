using Carousel.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Instagram;

/// <summary>
/// Development mock for <see cref="IInstagramPublisher"/>. Records each publish
/// in <see cref="InstagramMockOutbox"/> so the flow can be inspected via the
/// dev API. Can be armed to fail the next publish for retry tests.
/// </summary>
public sealed class MockInstagramPublisher : IInstagramPublisher
{
    private readonly InstagramMockOutbox _outbox;
    private readonly ILogger<MockInstagramPublisher> _logger;

    public MockInstagramPublisher(InstagramMockOutbox outbox, ILogger<MockInstagramPublisher> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public Task<PublishResult> PublishCarouselAsync(InstagramPublishRequest request, CancellationToken ct = default)
    {
        var slides = request.Cards
            .Select(c => new MockInstagramSlide(c.MediaAssetId, c.Text, c.Image?.LongLength))
            .ToArray();

        if (slides.Count(s => s.ImageBytes > 0) == 0)
        {
            _logger.LogWarning(
                "Instagram mock publish rejected for publication {PublicationId}: no image slides.",
                request.PublicationId);
            return Task.FromResult(new PublishResult(false, null, "Instagram carousel requires at least one image."));
        }

        if (_outbox.TryConsumeFail())
        {
            _logger.LogWarning(
                "Instagram mock forced failure for publication {PublicationId}.",
                request.PublicationId);
            return Task.FromResult(new PublishResult(false, null, "Mock forced failure."));
        }

        var externalId = $"ig-mock-{Guid.NewGuid():N}";
        var caption = string.Join(
            "\n",
            request.Cards
                .Select(c => c.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        _outbox.Enqueue(new MockInstagramPost(
            request.AccountExternalId,
            request.PublicationId,
            caption,
            slides,
            externalId,
            DateTime.UtcNow));

        _logger.LogInformation(
            "Instagram mock published publication {PublicationId} as {ExternalId} to {Account}.",
            request.PublicationId, externalId, request.AccountExternalId);

        return Task.FromResult(new PublishResult(true, externalId, null));
    }
}
