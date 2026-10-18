using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Instagram;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Carousel.Application.Tests;

public class MockInstagramPublisherTests
{
    private static MockInstagramPublisher CreatePublisher(out InstagramMockOutbox outbox)
    {
        outbox = new InstagramMockOutbox();
        return new MockInstagramPublisher(outbox, NullLogger<MockInstagramPublisher>.Instance);
    }

    private static InstagramPublishRequest RequestWithImages(int imageCount = 2, Guid? publicationId = null)
    {
        var cards = new List<PublishCard>();
        for (var i = 0; i < imageCount; i++)
        {
            cards.Add(new PublishCard($"Slide {i}", [1, 2, 3], "image/png", Guid.NewGuid()));
        }

        return new InstagramPublishRequest(publicationId ?? Guid.NewGuid(), cards, "ig-user-1");
    }

    [Fact]
    public async Task Publish_with_images_succeeds_and_records_outbox()
    {
        var publisher = CreatePublisher(out var outbox);
        var request = RequestWithImages(2);

        var result = await publisher.PublishCarouselAsync(request);

        result.Success.Should().BeTrue();
        result.ExternalPostId.Should().StartWith("ig-mock-");
        result.Error.Should().BeNull();

        var posts = outbox.Peek();
        posts.Should().HaveCount(1);
        posts[0].PublicationId.Should().Be(request.PublicationId);
        posts[0].AccountExternalId.Should().Be("ig-user-1");
        posts[0].Slides.Should().HaveCount(2);
        posts[0].Slides.Should().OnlyContain(s => s.ImageBytes == 3);
    }

    [Fact]
    public async Task Publish_without_images_fails()
    {
        var publisher = CreatePublisher(out var outbox);
        var request = new InstagramPublishRequest(
            Guid.NewGuid(),
            [new PublishCard("text only", null, null)],
            "ig-user-1");

        var result = await publisher.PublishCarouselAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("image");
        outbox.Peek().Should().BeEmpty();
    }

    [Fact]
    public async Task FailNext_forces_single_failure()
    {
        var publisher = CreatePublisher(out var outbox);
        outbox.ArmFailNext();

        var first = await publisher.PublishCarouselAsync(RequestWithImages(1));
        var second = await publisher.PublishCarouselAsync(RequestWithImages(1));

        first.Success.Should().BeFalse();
        first.Error.Should().Contain("forced");
        second.Success.Should().BeTrue();
        outbox.Peek().Should().HaveCount(1);
    }

    [Fact]
    public async Task Drain_clears_outbox()
    {
        var publisher = CreatePublisher(out var outbox);
        await publisher.PublishCarouselAsync(RequestWithImages(1));
        await publisher.PublishCarouselAsync(RequestWithImages(1));

        outbox.Drain().Should().HaveCount(2);
        outbox.Peek().Should().BeEmpty();
    }
}
