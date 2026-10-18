using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Logging;
using Carousel.Infrastructure.Publishing;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Bot;

/// <summary>
/// Thin Telegram Bot API client over HttpClient (getUpdates long polling + sendMessage).
/// No-op when bot token is empty (dev / CI without live bot).
/// </summary>
public sealed class TelegramBotApi : ITelegramBotApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<TelegramBotApi> _logger;
    private readonly string? _botUsernameOverride;
    private string? _botUsername; // кэш getMe

    public TelegramBotApi(HttpClient http, ILogger<TelegramBotApi> logger, string botToken, string? botUsernameOverride = null)
    {
        _http = http;
        _logger = logger;
        BotToken = botToken;
        _botUsernameOverride = string.IsNullOrWhiteSpace(botUsernameOverride)
            ? null
            : botUsernameOverride.Trim();
        if (!string.IsNullOrEmpty(botToken))
        {
            _http.BaseAddress = new Uri($"https://api.telegram.org/bot{botToken}/");
            _http.Timeout = TimeSpan.FromSeconds(65);
        }
    }

    public string BotToken { get; }

    public bool IsConfigured => !string.IsNullOrEmpty(BotToken);

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return Array.Empty<TelegramUpdate>();
        }

        var relativeUrl = $"getUpdates?offset={offset}&timeout=0&allowed_updates=%5B%22message%22,%22callback_query%22,%22business_connection%22%5D";
        try
        {
            var response = await _http.GetFromJsonAsync<BotApiResponse<List<TelegramUpdate>>>(
                relativeUrl,
                JsonOptions,
                ct);
            return (IReadOnlyList<TelegramUpdate>?)response?.Result ?? Array.Empty<TelegramUpdate>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", "GET", MaskUrl(relativeUrl));
            return Array.Empty<TelegramUpdate>();
        }
    }

    public async Task SendMessageAsync(long chatId, string text, IReadOnlyList<TelegramInlineButton>? inlineKeyboard = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        object payload = inlineKeyboard is { Count: > 0 }
            ? new
            {
                chat_id = chatId,
                text,
                reply_markup = new
                {
                    inline_keyboard = inlineKeyboard
                        .Select(b => new[] { new { text = b.Text, callback_data = b.CallbackData } })
                        .ToArray(),
                },
            }
            : new { chat_id = chatId, text };

        const string method = "POST";
        const string relativeUrl = "sendMessage";
        try
        {
            var resp = await _http.PostAsJsonAsync(relativeUrl, payload, JsonOptions, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Ошибка HTTP {Method} {Url}: {StatusCode} {Body}",
                    method, MaskUrl(relativeUrl), (int)resp.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
        }
    }

    public async Task SendPhotoAsync(long chatId, byte[] photo, string contentType, string? caption = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        const string method = "POST";
        const string relativeUrl = "sendPhoto";
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            if (!string.IsNullOrEmpty(caption))
            {
                form.Add(new StringContent(caption), "caption");
            }

            var extension = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
            var imageContent = new ByteArrayContent(photo);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(imageContent, "photo", $"card.{extension}");

            var resp = await _http.PostAsync(relativeUrl, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Ошибка HTTP {Method} {Url}: {StatusCode} {Body}",
                    method, MaskUrl(relativeUrl), (int)resp.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
        }
    }

    public async Task SendMediaGroupAsync(long chatId, IReadOnlyList<TelegramMediaGroupItem> items, CancellationToken ct = default)
    {
        if (!IsConfigured || items.Count == 0)
        {
            return;
        }

        const string method = "POST";
        const string relativeUrl = "sendMediaGroup";
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");

            var media = new List<object>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var key = $"file{i}";
                media.Add(new { type = "photo", media = $"attach://{key}", caption = item.Caption });

                var extension = item.ContentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
                var imageContent = new ByteArrayContent(item.Photo);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(item.ContentType);
                form.Add(imageContent, key, $"card{i}.{extension}");
            }

            form.Add(new StringContent(JsonSerializer.Serialize(media, JsonOptions)), "media");

            var resp = await _http.PostAsync(relativeUrl, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Ошибка HTTP {Method} {Url}: {StatusCode} {Body}",
                    method, MaskUrl(relativeUrl), (int)resp.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
        }
    }

    public async Task<TelegramApiResult> TryPostStoryAsync(
        string businessConnectionId, byte[] photo, string contentType, string? caption,
        string? linkUrl = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return TelegramApiResult.Fail("BOT_NOT_CONFIGURED", "Bot token is not configured.");
        }

        const string relativeUrl = "postStory";
        try
        {
            // Ссылка на бота: видимая надпись на фото + кликабельная зона в правом нижнем углу.
            var image = photo;
            var stampText = PrettyLinkText(linkUrl);
            if (stampText is not null)
            {
                image = StoryLinkStamp.Stamp(image, contentType, stampText);
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(businessConnectionId), "business_connection_id");
            form.Add(new StringContent("86400"), "active_period");

            // InputStoryContent as JSON, image attached as file0.
            var contentJson = JsonSerializer.Serialize(
                new { type = "photo", media = "attach://file0" }, JsonOptions);
            form.Add(new StringContent(contentJson), "content");

            if (!string.IsNullOrEmpty(caption))
            {
                form.Add(new StringContent(caption), "caption");
            }

            if (linkUrl is not null)
            {
                // Центр зоны (x/y_percentage) — под печатной пилюлей в правом нижнем углу.
                var areas = JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            position = new
                            {
                                x_percentage = 80d,
                                y_percentage = 93d,
                                width_percentage = 36d,
                                height_percentage = 10d,
                                rotation_angle = 0d,
                                corner_radius_percentage = 50d,
                            },
                            type = new { type = "link", url = linkUrl },
                        },
                    },
                    JsonOptions);
                form.Add(new StringContent(areas), "areas");
            }

            var imageContent = new ByteArrayContent(image);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(imageContent, "file0", contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "story.png" : "story.jpg");

            var response = await _http.PostAsync(relativeUrl, form, ct);
            var body = await response.Content.ReadFromJsonAsync<BotApiResponse<JsonElement>>(
                JsonOptions, ct);

            if (body is { Ok: true })
            {
                return TelegramApiResult.Success;
            }

            var code = body?.ErrorCode?.ToString() ?? ((int)response.StatusCode).ToString();
            var description = body?.Description ?? $"HTTP {(int)response.StatusCode}";
            _logger.LogWarning(
                "Ошибка HTTP {Method} {Url}: {StatusCode} {Code} {Description}",
                "POST", MaskUrl(relativeUrl), (int)response.StatusCode, code, description);
            return TelegramApiResult.Fail(code, description);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", "POST", MaskUrl(relativeUrl));
            return TelegramApiResult.Fail("HTTP", ex.Message);
        }
    }

    public async Task<string?> CreateInvoiceLinkAsync(
        string title, string description, string payload, int amountStars, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        const string method = "POST";
        const string relativeUrl = "createInvoiceLink";
        try
        {
            object invoice = new
            {
                title,
                description,
                payload,
                currency = "XTR",
                prices = new[] { new { label = title, amount = amountStars } },
            };

            var resp = await _http.PostAsJsonAsync(relativeUrl, invoice, JsonOptions, ct);
            var body = await resp.Content.ReadFromJsonAsync<BotApiResponse<string>>(JsonOptions, ct);
            if (resp.IsSuccessStatusCode && body is { Ok: true } && !string.IsNullOrEmpty(body.Result))
            {
                return body.Result;
            }

            _logger.LogWarning(
                "Ошибка HTTP {Method} {Url}: {StatusCode} {Code} {Body}",
                method, MaskUrl(relativeUrl), (int)resp.StatusCode,
                body?.ErrorCode?.ToString() ?? "n/a",
                body?.Description ?? string.Empty);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
            return null;
        }
    }

    public async Task<TelegramApiResult> RefundStarPaymentAsync(long userId, string chargeId, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return TelegramApiResult.Fail("BOT_NOT_CONFIGURED", "Bot token is not configured.");
        }

        const string method = "POST";
        const string relativeUrl = "refundStarPayment";
        try
        {
            var refund = new { user_id = userId, telegram_payment_charge_id = chargeId };
            var resp = await _http.PostAsJsonAsync(relativeUrl, refund, JsonOptions, ct);
            var body = await resp.Content.ReadFromJsonAsync<BotApiResponse<bool>>(JsonOptions, ct);
            if (resp.IsSuccessStatusCode && body is { Ok: true })
            {
                return TelegramApiResult.Success;
            }

            var code = body?.ErrorCode?.ToString() ?? ((int)resp.StatusCode).ToString();
            var description = body?.Description ?? $"HTTP {(int)resp.StatusCode}";
            _logger.LogWarning(
                "Ошибка HTTP {Method} {Url}: {StatusCode} {Code} {Description}",
                method, MaskUrl(relativeUrl), (int)resp.StatusCode, code, description);
            return TelegramApiResult.Fail(code, description);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
            return TelegramApiResult.Fail("HTTP", ex.Message);
        }
    }

    public async Task<TelegramChatMember?> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var relativeUrl = $"getChatMember?chat_id={chatId}&user_id={userId}";
        const string method = "GET";
        try
        {
            var response = await _http.GetFromJsonAsync<BotApiResponse<ChatMemberResult>>(
                relativeUrl, JsonOptions, ct);
            var result = response?.Result;
            if (result is null || string.IsNullOrEmpty(result.Status))
            {
                return null;
            }

            return new TelegramChatMember(result.UserId ?? userId, result.Status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
            return null;
        }
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        object payload = string.IsNullOrEmpty(text)
            ? new Dictionary<string, string> { ["callback_query_id"] = callbackQueryId }
            : new Dictionary<string, string> { ["callback_query_id"] = callbackQueryId, ["text"] = text };

        const string method = "POST";
        const string relativeUrl = "answerCallbackQuery";
        try
        {
            var resp = await _http.PostAsJsonAsync(relativeUrl, payload, JsonOptions, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Ошибка HTTP {Method} {Url}: {StatusCode} {Body}",
                    method, MaskUrl(relativeUrl), (int)resp.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", method, MaskUrl(relativeUrl));
        }
    }

    public async Task<string> GetBotUsernameAsync(CancellationToken ct = default)
    {
        // Приоритет: оверрайд конфига (два бота: тестовый/прод) → кэш getMe → заглушка.
        if (_botUsernameOverride is not null)
        {
            return _botUsernameOverride;
        }

        if (!string.IsNullOrEmpty(_botUsername))
        {
            return _botUsername;
        }

        if (!IsConfigured)
        {
            return "mockbot";
        }

        try
        {
            var resp = await _http.GetFromJsonAsync<BotApiResponse<GetMeUser>>(
                "getMe", JsonOptions, ct);
            var username = resp?.Result?.Username;
            if (!string.IsNullOrEmpty(username))
            {
                _botUsername = username;
                return username;
            }

            _logger.LogWarning("getMe не вернул username.");
            return string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ошибка HTTP {Method} {Url}", "GET", "getMe");
            return string.Empty;
        }
    }

    /// <summary>"https://t.me/xxx" → "t.me/xxx"; иначе null (не печатать).</summary>
    private static string? PrettyLinkText(string? linkUrl)
    {
        if (string.IsNullOrWhiteSpace(linkUrl))
        {
            return null;
        }

        return linkUrl
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("tg://", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private string MaskUrl(string relativeUrl)
    {
        var absolute = _http.BaseAddress is null ? relativeUrl : new Uri(_http.BaseAddress, relativeUrl).ToString();
        return UrlLogMasker.MaskBotToken(absolute);
    }
}

internal sealed class BotApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class ChatMemberResult
{
    [JsonPropertyName("user")]
    public ChatMemberUser? User { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    public long? UserId => User?.Id;
}

internal sealed class ChatMemberUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

internal sealed class GetMeUser
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}
