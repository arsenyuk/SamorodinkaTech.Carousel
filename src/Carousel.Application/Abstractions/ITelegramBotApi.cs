using System.Text.Json.Serialization;

namespace Carousel.Application.Abstractions;

/// <summary>Thin Telegram Bot API surface used by long-polling host and publish.</summary>
public interface ITelegramBotApi
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct = default);
    Task SendMessageAsync(long chatId, string text, IReadOnlyList<TelegramInlineButton>? inlineKeyboard = null, CancellationToken ct = default);
    Task SendPhotoAsync(long chatId, byte[] photo, string contentType, string? caption = null, CancellationToken ct = default);
    Task SendMediaGroupAsync(long chatId, IReadOnlyList<TelegramMediaGroupItem> items, CancellationToken ct = default);
    Task<TelegramApiResult> TryPostStoryAsync(string businessConnectionId, byte[] photo, string contentType, string? caption, string? linkUrl = null, CancellationToken ct = default);
    /// <summary>
    /// Username бота для ссылок: оверрайд Telegram:BotUsername → getMe-кэш → "mockbot".
    /// </summary>
    Task<string> GetBotUsernameAsync(CancellationToken ct = default);
    /// <returns>Invoice link (t.me/invoice/…) или null при ошибке API (детали — в логе).</returns>
    Task<string?> CreateInvoiceLinkAsync(string title, string description, string payload, int amountStars, CancellationToken ct = default);
    /// <summary>Возврат успешной оплаты Stars (Bot API refundStarPayment).</summary>
    Task<TelegramApiResult> RefundStarPaymentAsync(long userId, string chargeId, CancellationToken ct = default);
    Task<TelegramChatMember?> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default);
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default);
}

/// <summary>Result of a Bot API call where the caller needs the Telegram error code/description.</summary>
public sealed record TelegramApiResult(bool Ok, string? Code, string? Description)
{
    public static readonly TelegramApiResult Success = new(true, null, null);

    public static TelegramApiResult Fail(string code, string? description) => new(false, code, description);
}

/// <summary>Business connection (user added the bot in Telegram Settings → Business).</summary>
public sealed record TelegramBusinessConnection(string Id, long UserId, string? Username, bool IsEnabled);

[JsonConverter(typeof(TelegramUpdateJsonConverter))]
public sealed record TelegramUpdate(
    long UpdateId,
    TelegramMessage? Message,
    TelegramCallbackQuery? CallbackQuery,
    TelegramBusinessConnection? BusinessConnection = null);

public sealed record TelegramMessage(
    long MessageId,
    long ChatId,
    long FromUserId,
    string? FromUsername,
    string? FromFirstName,
    string Text,
    TelegramSuccessfulPayment? SuccessfulPayment = null);

/// <summary>message.successful_payment — оплата инвойса звёздами (XTR).</summary>
public sealed record TelegramSuccessfulPayment(
    string Payload,
    long Amount,
    string Currency,
    string ChargeId);

public sealed record TelegramCallbackQuery(
    string Id,
    long FromUserId,
    string FromUsername,
    string Data);

public sealed record TelegramInlineButton(string Text, string CallbackData);

public sealed record TelegramMediaGroupItem(byte[] Photo, string ContentType, string? Caption);

/// <summary>Subset of Bot API ChatMember used for admin checks.</summary>
public sealed record TelegramChatMember(long UserId, string Status)
{
    public bool IsAdmin => Status is "creator" or "administrator";
}
