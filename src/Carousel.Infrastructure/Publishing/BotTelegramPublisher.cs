using Carousel.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Publishing;

/// <summary>
/// Publishes carousel slides as Telegram stories (postStory) via the user's business
/// connection. The bot chat receives nothing — success/error travels only through
/// <see cref="PublishResult"/> to the Mini App.
/// </summary>
public sealed class BotTelegramPublisher : ITelegramPublisher
{
    // Telegram: story caption is 0-2048 characters.
    private const int MaxStoryCaptionLength = 2048;

    private readonly ITelegramBotApi _bot;
    private readonly ITelegramBusinessConnectionStore _connections;
    private readonly ILogger<BotTelegramPublisher> _logger;

    public BotTelegramPublisher(
        ITelegramBotApi bot,
        ITelegramBusinessConnectionStore connections,
        ILogger<BotTelegramPublisher> logger)
    {
        _bot = bot;
        _connections = connections;
        _logger = logger;
    }

    public async Task<PublishResult> PublishCarouselAsync(TelegramPublishRequest request, CancellationToken ct = default)
    {
        // publish/me and retry pass the publisher's Telegram user id here.
        if (!long.TryParse(request.ChannelExternalId, out var userId) || userId == 0)
        {
            return Fail("BAD_CHANNEL", "Некорректный id пользователя Telegram.");
        }

        if (!_bot.IsConfigured)
        {
            _logger.LogWarning("Telegram publish skipped: bot token is not configured.");
            return Fail("BOT_NOT_CONFIGURED", "Telegram bot is not configured.");
        }

        try
        {
            var connection = await _connections.GetEnabledConnectionAsync(userId, ct);
            if (connection is null)
            {
                return Fail(
                    "NO_BUSINESS_CONNECTION",
                    "Бот не подключён к бизнес-аккаунту: Настройки Telegram → Бизнес → бот.");
            }

            // Ссылка на бота (реклама): видимая надпись + кликабельная зона в каждой истории.
            // Крыжек «Отключить рекламу бота» (request.NoAds) — без ссылки.
            string? linkUrl;
            if (request.NoAds)
            {
                linkUrl = null;
                _logger.LogInformation(
                    "Реклама бота отключена для публикации {PublicationId} — истории публикуются без ссылки.",
                    request.PublicationId);
            }
            else
            {
                var botUsername = await _bot.GetBotUsernameAsync(ct);
                linkUrl = string.IsNullOrWhiteSpace(botUsername)
                    ? null
                    : $"https://t.me/{botUsername}";
                if (linkUrl is null)
                {
                    _logger.LogWarning(
                        "Bot username unavailable — истории будут опубликованы без ссылки на бота.");
                }
            }

            var posted = 0;
            foreach (var card in request.Cards)
            {
                if (card.Image is not { Length: > 0 })
                {
                    continue;
                }

                // Подпись — только у настоящих фото; у рендеренных карточек текст уже на изображении (без дубля).
                var caption = card.MediaAssetId is not null && !string.IsNullOrWhiteSpace(card.Text)
                    ? card.Text
                    : null;
                if (caption is { Length: > MaxStoryCaptionLength })
                {
                    caption = caption[..MaxStoryCaptionLength];
                }

                var result = await _bot.TryPostStoryAsync(
                    connection.Id, card.Image, card.ContentType ?? "image/jpeg", caption, linkUrl, ct);
                if (!result.Ok)
                {
                    _logger.LogWarning(
                        "Story post failed for publication {PublicationId}: {Code} {Description}",
                        request.PublicationId, result.Code, result.Description);
                    return Fail(result.Code ?? "STORY_FAILED", result.Description ?? "Story post failed.");
                }

                posted++;
            }

            if (posted == 0)
            {
                return Fail("NO_CONTENT", "Нет слайдов с изображениями для истории.");
            }

            var externalId = $"tg-story-{Guid.NewGuid():N}";
            _logger.LogInformation(
                "Published {Posted} stories for publication {PublicationId} via connection {ConnectionId} as {ExternalId}.",
                posted, request.PublicationId, connection.Id, externalId);
            return new PublishResult(true, externalId, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Telegram story publish failed for publication {PublicationId}.",
                request.PublicationId);
            return Fail("INTERNAL", ex.Message);
        }
    }

    private static PublishResult Fail(string code, string description) =>
        new(false, null, $"Ошибка публикации. {code}: {description}");
}