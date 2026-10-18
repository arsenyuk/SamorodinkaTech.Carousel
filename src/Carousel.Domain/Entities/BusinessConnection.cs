namespace Carousel.Domain.Entities;

/// <summary>
/// Telegram business connection: the user added the bot in Telegram Settings → Business.
/// The bot learns the connection id via the business_connection update.
/// </summary>
public sealed class BusinessConnection
{
    public Guid Id { get; set; }

    public string ConnectionId { get; set; } = string.Empty;

    public long TelegramUserId { get; set; }

    public string? Username { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}