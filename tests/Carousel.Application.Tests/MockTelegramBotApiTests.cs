using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Bot;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class MockTelegramBotApiTests
{
    [Fact]
    public async Task Enqueued_message_is_delivered_via_getUpdates()
    {
        var mock = new MockTelegramBotApi();
        mock.EnqueueUserMessage(42, "/start", "devuser", "Dev");

        var updates = await mock.GetUpdatesAsync(0);
        updates.Should().HaveCount(1);
        var msg = updates[0].Message;
        msg.Should().NotBeNull();
        updates[0].UpdateId.Should().Be(1);
        msg!.FromUserId.Should().Be(42);
        msg.ChatId.Should().Be(42);
        msg.Text.Should().Be("/start");
        msg.FromUsername.Should().Be("devuser");
    }

    [Fact]
    public async Task GetUpdates_respects_offset_and_does_not_redeliver()
    {
        var mock = new MockTelegramBotApi();
        mock.EnqueueUserMessage(1, "a");
        mock.EnqueueUserMessage(1, "b");

        var first = await mock.GetUpdatesAsync(0);
        first.Should().HaveCount(2);
        var nextOffset = first.Max(u => u.UpdateId) + 1;

        var second = await mock.GetUpdatesAsync(nextOffset);
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessage_lands_in_outbox()
    {
        var mock = new MockTelegramBotApi();
        await mock.SendMessageAsync(
            99,
            "hello",
            new[] { new TelegramInlineButton("Open", "open_preview:abc") });

        var outbox = mock.PeekOutbox();
        outbox.Should().HaveCount(1);
        outbox[0].ChatId.Should().Be(99);
        outbox[0].Text.Should().Be("hello");
        outbox[0].InlineKeyboard.Should().ContainSingle(b => b.CallbackData == "open_preview:abc");
    }

    [Fact]
    public async Task SendMediaGroup_lands_in_outbox_with_items()
    {
        var mock = new MockTelegramBotApi();
        var items = new[]
        {
            new TelegramMediaGroupItem(new byte[] { 1, 2 }, "image/jpeg", "Слайд 1"),
            new TelegramMediaGroupItem(new byte[] { 3, 4, 5 }, "image/png", "Слайд 2"),
            new TelegramMediaGroupItem(new byte[] { 6 }, "image/jpeg", null),
        };

        await mock.SendMediaGroupAsync(77, items);

        var outbox = mock.PeekOutbox();
        outbox.Should().HaveCount(1);
        outbox[0].ChatId.Should().Be(77);
        outbox[0].Text.Should().StartWith("[mediagroup 3]");
        outbox[0].Text.Should().Contain("[photo 2B image/jpeg] Слайд 1");
        outbox[0].Text.Should().Contain("[photo 3B image/png] Слайд 2");
        outbox[0].Text.Should().Contain("[photo 1B image/jpeg]");
    }

    [Fact]
    public async Task DrainOutbox_clears_bot_replies()
    {
        var mock = new MockTelegramBotApi();
        await mock.SendMessageAsync(1, "reply-1");
        await mock.SendMessageAsync(1, "reply-2");

        var drained = mock.DrainOutbox();
        drained.Should().HaveCount(2);
        mock.PeekOutbox().Should().BeEmpty();
    }

    [Fact]
    public void IsConfigured_is_always_true()
    {
        new MockTelegramBotApi().IsConfigured.Should().BeTrue();
    }
}
