using System.Collections.Concurrent;
using Carousel.Application.Abstractions;

namespace Carousel.Infrastructure.Bot;

/// <summary>
/// In-process Telegram Bot API mock for local development without a live bot token.
/// Users enqueue messages via the mock API; the long-polling host consumes them
/// through <see cref="GetUpdatesAsync"/>; bot replies land in an outbox.
/// </summary>
public sealed class MockTelegramBotApi : ITelegramBotApi
{
    private readonly ConcurrentQueue<TelegramUpdate> _inbox = new();
    private readonly ConcurrentQueue<MockOutboxMessage> _outbox = new();
    private readonly string? _botUsernameOverride;
    private long _nextUpdateId;
    private long _nextMessageId;
    private long _nextCallbackId;

    public MockTelegramBotApi(string? botUsernameOverride = null)
    {
        _botUsernameOverride = string.IsNullOrWhiteSpace(botUsernameOverride)
            ? null
            : botUsernameOverride.Trim();
    }

    public bool IsConfigured => true;

    /// <summary>Оверрайд конфига (тестовый бот в dev) или заглушка mockbot.</summary>
    public Task<string> GetBotUsernameAsync(CancellationToken ct = default) =>
        Task.FromResult(_botUsernameOverride ?? "mockbot");

    public void EnqueueUserMessage(
        long fromUserId,
        string text,
        string? fromUsername = null,
        string? fromFirstName = null)
    {
        var updateId = Interlocked.Increment(ref _nextUpdateId);
        var messageId = Interlocked.Increment(ref _nextMessageId);
        _inbox.Enqueue(new TelegramUpdate(
            updateId,
            new TelegramMessage(messageId, fromUserId, fromUserId, fromUsername, fromFirstName, text),
            null));
    }

    public void EnqueueCallback(long fromUserId, string data, string? fromUsername = null)
    {
        var updateId = Interlocked.Increment(ref _nextUpdateId);
        var callbackId = Interlocked.Increment(ref _nextCallbackId);
        _inbox.Enqueue(new TelegramUpdate(
            updateId,
            null,
            new TelegramCallbackQuery($"cbq-{callbackId}", fromUserId, fromUsername ?? "devuser", data)));
    }

    public IReadOnlyList<MockOutboxMessage> PeekOutbox() => _outbox.ToArray();

    public IReadOnlyList<MockOutboxMessage> DrainOutbox()
    {
        var list = new List<MockOutboxMessage>();
        while (_outbox.TryDequeue(out var item))
        {
            list.Add(item);
        }

        return list;
    }

    public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct = default)
    {
        var result = new List<TelegramUpdate>();
        while (_inbox.TryPeek(out var update))
        {
            if (update.UpdateId < offset)
            {
                _inbox.TryDequeue(out _);
                continue;
            }

            if (!_inbox.TryDequeue(out update))
            {
                break;
            }

            result.Add(update);
        }

        if (result.Count == 0)
        {
            // Avoid busy-looping the long-polling host when there is nothing to do.
            return Task.Delay(TimeSpan.FromMilliseconds(250), ct).ContinueWith(
                _ => (IReadOnlyList<TelegramUpdate>)Array.Empty<TelegramUpdate>(),
                ct);
        }

        return Task.FromResult<IReadOnlyList<TelegramUpdate>>(result);
    }

    public Task SendMessageAsync(
        long chatId,
        string text,
        IReadOnlyList<TelegramInlineButton>? inlineKeyboard = null,
        CancellationToken ct = default)
    {
        _outbox.Enqueue(new MockOutboxMessage(
            chatId,
            text,
            inlineKeyboard?
                .Select(b => new MockInlineButton(b.Text, b.CallbackData))
                .ToArray() ?? Array.Empty<MockInlineButton>(),
            DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task SendPhotoAsync(long chatId, byte[] photo, string contentType, string? caption = null, CancellationToken ct = default)
    {
        _outbox.Enqueue(new MockOutboxMessage(
            chatId,
            $"[photo {photo.Length}B {contentType}] {caption}",
            Array.Empty<MockInlineButton>(),
            DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task SendMediaGroupAsync(long chatId, IReadOnlyList<TelegramMediaGroupItem> items, CancellationToken ct = default)
    {
        var text = $"[mediagroup {items.Count}] " + string.Join(
            " | ",
            items.Select(i => $"[photo {i.Photo.Length}B {i.ContentType}] {i.Caption}"));
        _outbox.Enqueue(new MockOutboxMessage(
            chatId,
            text,
            Array.Empty<MockInlineButton>(),
            DateTime.UtcNow));
        return Task.CompletedTask;
    }

    /// <summary>When set, TryPostStoryAsync returns this error instead of posting (test hook).</summary>
    public TelegramApiResult? StoryErrorToReturn { get; set; }

    public Task<TelegramApiResult> TryPostStoryAsync(
        string businessConnectionId, byte[] photo, string contentType, string? caption,
        string? linkUrl = null, CancellationToken ct = default)
    {
        if (StoryErrorToReturn is not null)
        {
            return Task.FromResult(StoryErrorToReturn);
        }

        _outbox.Enqueue(new MockOutboxMessage(
            0,
            $"[story {businessConnectionId}]{(linkUrl is null ? "" : $" link={linkUrl}")} [photo {photo.Length}B {contentType}] {caption}",
            Array.Empty<MockInlineButton>(),
            DateTime.UtcNow));
        return Task.FromResult(TelegramApiResult.Success);
    }

    public Task<string?> CreateInvoiceLinkAsync(
        string title, string description, string payload, int amountStars, CancellationToken ct = default) =>
        Task.FromResult<string?>($"https://t.me/invoice/mock-{Guid.NewGuid():N}");

    public Task<TelegramApiResult> RefundStarPaymentAsync(long userId, string chargeId, CancellationToken ct = default) =>
        Task.FromResult(TelegramApiResult.Success);

    public Task<TelegramChatMember?> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default)
    {
        // Dev mock: always treat the user as chat administrator (non-admin path is for live bot only).
        return Task.FromResult<TelegramChatMember?>(new TelegramChatMember(userId, "administrator"));
    }

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default)
    {
        _outbox.Enqueue(new MockOutboxMessage(
            0,
            $"[callback answer {callbackQueryId}] {text}",
            Array.Empty<MockInlineButton>(),
            DateTime.UtcNow));
        return Task.CompletedTask;
    }
}

public sealed record MockOutboxMessage(
    long ChatId,
    string Text,
    IReadOnlyList<MockInlineButton> InlineKeyboard,
    DateTime AtUtc);

public sealed record MockInlineButton(string Text, string CallbackData);
