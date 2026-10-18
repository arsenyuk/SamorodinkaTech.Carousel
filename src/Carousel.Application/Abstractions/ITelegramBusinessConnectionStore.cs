namespace Carousel.Application.Abstractions;

/// <summary>
/// Lookup of the enabled Telegram business connection for a user
/// (the user connected the bot in Telegram Settings → Business).
/// </summary>
public interface ITelegramBusinessConnectionStore
{
    Task<TelegramBusinessConnection?> GetEnabledConnectionAsync(long telegramUserId, CancellationToken ct = default);
}