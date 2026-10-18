using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Auth;

/// <summary>
/// Stage 1 stub: extracts a pseudo user id from initData without cryptographic validation.
/// TODO(Этап 3): validate Telegram initData with bot token HMAC.
/// </summary>
public sealed class StubTelegramAuthService : ITelegramAuthService
{
    private readonly CarouselDbContext _db;

    public StubTelegramAuthService(CarouselDbContext db)
    {
        _db = db;
    }

    public async Task<TelegramAuthResult> AuthenticateAsync(string initData, CancellationToken ct = default)
    {
        long telegramUserId;
        string? username = null;
        string? displayName = null;

        if (initData.Contains('=') && initData.Contains('&'))
        {
            var parsed = ParseInitData(initData);
            if (!parsed.TryGetValue("user", out var userJson))
            {
                throw new InvalidOperationException("initData is missing user field.");
            }

            using var doc = JsonDocument.Parse(userJson);
            telegramUserId = doc.RootElement.GetProperty("id").GetInt64();
            if (doc.RootElement.TryGetProperty("username", out var u))
            {
                username = u.GetString();
            }

            if (doc.RootElement.TryGetProperty("first_name", out var fn))
            {
                displayName = fn.GetString();
            }
        }
        else if (long.TryParse(initData, out telegramUserId))
        {
            // Local debug: allow raw Telegram user id as initData.
        }
        else
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(initData));
            telegramUserId = BitConverter.ToInt64(hash, 0) & long.MaxValue;
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        if (user is null)
        {
            user = new Domain.Entities.User
            {
                TelegramUserId = telegramUserId,
                TelegramUsername = username,
                DisplayName = displayName,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else if (username is not null || displayName is not null)
        {
            user.TelegramUsername = username ?? user.TelegramUsername;
            user.DisplayName = displayName ?? user.DisplayName;
            await _db.SaveChangesAsync(ct);
        }

        return new TelegramAuthResult(user.TelegramUserId, user.TelegramUsername, user.DisplayName);
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
