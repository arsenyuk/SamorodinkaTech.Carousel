using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Application.Security;

namespace Carousel.Application.Services;

/// <summary>
/// Validates Telegram Mini App initData per
/// https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public sealed class TelegramInitDataValidator : IInitDataValidator
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly string _botToken;
    private readonly bool _allowDevRawId;
    private readonly bool _isDevelopment;

    public TelegramInitDataValidator(string botToken, bool allowDevRawId, bool isDevelopment)
    {
        _botToken = botToken;
        _allowDevRawId = allowDevRawId;
        _isDevelopment = isDevelopment;
    }

    public InitDataUser Validate(string initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            throw new ValidationException("initData is required.");
        }

        // Query-string form from Telegram WebApp / Mini App.
        if (initData.Contains('=') && initData.Contains('&'))
        {
            var pairs = ParseInitData(initData);
            if (!pairs.TryGetValue("hash", out var hash) || string.IsNullOrEmpty(hash))
            {
                throw new ValidationException("initData is missing hash.");
            }

            if (!string.IsNullOrEmpty(_botToken))
            {
                VerifyHmac(pairs, hash);
            }
            else if (!(_isDevelopment && _allowDevRawId))
            {
                throw new ValidationException("Bot token is not configured; cannot validate initData.");
            }

            if (!pairs.TryGetValue("user", out var userJson))
            {
                throw new ValidationException("initData is missing user field.");
            }

            var user = ParseUser(userJson);

            // Mini App launched from a chat carries chat_id + chat_type.
            long? chatId = null;
            string? chatType = null;
            if (pairs.TryGetValue("chat_id", out var chatIdRaw) && long.TryParse(chatIdRaw, out var parsedChatId))
            {
                chatId = parsedChatId;
            }

            if (pairs.TryGetValue("chat_type", out var chatTypeRaw) && !string.IsNullOrWhiteSpace(chatTypeRaw))
            {
                chatType = chatTypeRaw;
            }

            return user with { ChatId = chatId, ChatType = chatType };
        }

        // Development-only: raw Telegram user id (integration tests / local debug).
        if (_isDevelopment && _allowDevRawId && long.TryParse(initData, out var rawId))
        {
            // Dev-логин по голому id: фикстурный username — алиас exempt от оплаты Stars
            // (Billing:FreeUsernames = ["devuser"] в Development).
            return new InitDataUser(rawId, "devuser", "Dev");
        }

        throw new ValidationException("Unsupported initData format.");
    }

    private void VerifyHmac(Dictionary<string, string> pairs, string receivedHash)
    {
        var dataCheckPairs = pairs
            .Where(kv => kv.Key != "hash")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}");
        var dataCheckString = string.Join('\n', dataCheckPairs);

        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(_botToken));
        var expected = Convert.ToHexString(
            HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

        var received = receivedHash.ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(received)))
        {
            throw new ValidationException("initData signature is invalid.");
        }

        if (pairs.TryGetValue("auth_date", out var authDateRaw) && long.TryParse(authDateRaw, out var authDateUnix))
        {
            var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
            if (DateTimeOffset.UtcNow - authDate > MaxAge)
            {
                throw new ValidationException("initData has expired.");
            }
        }
    }

    private static InitDataUser ParseUser(string userJson)
    {
        using var doc = JsonDocument.Parse(userJson);
        if (!doc.RootElement.TryGetProperty("id", out var idEl))
        {
            throw new ValidationException("initData user is missing id.");
        }

        var id = idEl.GetInt64();
        string? username = null;
        string? firstName = null;
        if (doc.RootElement.TryGetProperty("username", out var u))
        {
            username = u.GetString();
        }

        if (doc.RootElement.TryGetProperty("first_name", out var fn))
        {
            firstName = fn.GetString();
        }

        return new InitDataUser(id, username, firstName);
    }

    private static Dictionary<string, string> ParseInitData(string initData)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
