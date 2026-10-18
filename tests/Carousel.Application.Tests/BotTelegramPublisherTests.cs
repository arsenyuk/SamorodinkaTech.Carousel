using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Bot;
using Carousel.Infrastructure.Publishing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Carousel.Application.Tests;

public class BotTelegramPublisherTests
{
    private sealed class FakeConnections : ITelegramBusinessConnectionStore
    {
        public TelegramBusinessConnection? Connection { get; set; } =
            new("conn-1", 42, "devuser", true);

        public Task<TelegramBusinessConnection?> GetEnabledConnectionAsync(
            long telegramUserId, CancellationToken ct = default) =>
            Task.FromResult(Connection);
    }

    private static (BotTelegramPublisher Publisher, MockTelegramBotApi Mock, FakeConnections Connections) Create()
    {
        var mock = new MockTelegramBotApi();
        var connections = new FakeConnections();
        var publisher = new BotTelegramPublisher(
            mock, connections, NullLogger<BotTelegramPublisher>.Instance);
        return (publisher, mock, connections);
    }

    private static TelegramPublishRequest Request(params PublishCard[] cards) =>
        new(Guid.NewGuid(), "Карусель", null, cards, "42");

    private static TelegramPublishRequest RequestNoAds(params PublishCard[] cards) =>
        new(Guid.NewGuid(), "Карусель", null, cards, "42", NoAds: true);

    // Настоящее фото (есть MediaAssetId) — только для него текст уходит в caption сторис.
    private static PublishCard PhotoCard(string? text = "текст") =>
        new(text, new byte[] { 1, 2, 3 }, "image/jpeg", Guid.NewGuid());

    private static PublishCard TextCard(string? text = null) =>
        new(text, null, null);

    [Fact]
    public async Task Publish_posts_each_slide_as_story_and_keeps_chat_silent()
    {
        var (publisher, mock, _) = Create();

        var result = await publisher.PublishCarouselAsync(
            Request(PhotoCard("a"), TextCard(), PhotoCard("b")));

        result.Success.Should().BeTrue();
        result.ExternalPostId.Should().StartWith("tg-story-");

        var outbox = mock.PeekOutbox();
        outbox.Should().HaveCount(2, "в чат не должно уходить никаких сообщений");
        outbox.Should().OnlyContain(m => m.Text.StartsWith("[story conn-1]"));
        outbox.Should().OnlyContain(m => m.Text.Contains("link=https://t.me/mockbot"), "по умолчанию реклама бота есть");
        outbox[0].Text.Should().Contain("a");
        outbox[1].Text.Should().Contain("b");
    }

    [Fact]
    public async Task Publish_with_noAds_does_not_link_bot()
    {
        var (publisher, mock, _) = Create();

        var result = await publisher.PublishCarouselAsync(RequestNoAds(PhotoCard("a"), PhotoCard("b")));

        result.Success.Should().BeTrue();
        var outbox = mock.PeekOutbox();
        outbox.Should().HaveCount(2);
        outbox.Should().OnlyContain(m => m.Text.StartsWith("[story conn-1]"));
        outbox.Should().NotContain(m => m.Text.Contains("link="), "реклама отключена — ссылки на бота нет");
    }

    [Fact]
    public async Task Publish_without_business_connection_fails_without_chat_messages()
    {
        var (publisher, mock, connections) = Create();
        connections.Connection = null;

        var result = await publisher.PublishCarouselAsync(Request(PhotoCard()));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("NO_BUSINESS_CONNECTION");
        mock.PeekOutbox().Should().BeEmpty("чат должен молчать");
    }

    [Fact]
    public async Task Publish_story_error_is_surfaced_with_code()
    {
        var (publisher, mock, _) = Create();
        mock.StoryErrorToReturn = TelegramApiResult.Fail("400", "Bad Request: STORY_TOO_OLD");

        var result = await publisher.PublishCarouselAsync(Request(PhotoCard(), PhotoCard()));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Ошибка публикации. 400");
        result.Error.Should().Contain("STORY_TOO_OLD");
        mock.PeekOutbox().Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_without_photo_cards_fails_no_content()
    {
        var (publisher, mock, _) = Create();

        var result = await publisher.PublishCarouselAsync(Request(TextCard("только текст")));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("NO_CONTENT");
        mock.PeekOutbox().Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_truncates_story_caption_to_2048()
    {
        var (publisher, mock, _) = Create();
        var longText = new string('х', 3000);

        var result = await publisher.PublishCarouselAsync(Request(PhotoCard(longText)));

        result.Success.Should().BeTrue();
        var outbox = mock.PeekOutbox();
        outbox.Should().HaveCount(1);
        outbox[0].Text.Should().EndWith(longText[..2048]);
        outbox[0].Text.Should().NotContain(longText);
    }

    [Fact]
    public async Task Publish_bad_channel_id_fails()
    {
        var (publisher, mock, _) = Create();
        var request = new TelegramPublishRequest(
            Guid.NewGuid(), "Карусель", null, new[] { PhotoCard() }, "not-a-number");

        var result = await publisher.PublishCarouselAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("BAD_CHANNEL");
        mock.PeekOutbox().Should().BeEmpty();
    }
}