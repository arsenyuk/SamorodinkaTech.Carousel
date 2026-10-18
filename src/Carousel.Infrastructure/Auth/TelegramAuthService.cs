using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Auth;

/// <summary>
/// Stage 3: HMAC-validated Telegram initData auth + user provisioning.
/// Dev-only raw Telegram id is handled by <see cref="IInitDataValidator"/> when enabled.
/// </summary>
public sealed class TelegramAuthService : ITelegramAuthService
{
    private readonly CarouselDbContext _db;
    private readonly IInitDataValidator _validator;

    public TelegramAuthService(CarouselDbContext db, IInitDataValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<TelegramAuthResult> AuthenticateAsync(string initData, CancellationToken ct = default)
    {
        var parsed = _validator.Validate(initData);

        var user = await _db.Users.FirstOrDefaultAsync(x => x.TelegramUserId == parsed.TelegramUserId, ct);
        if (user is null)
        {
            user = new Domain.Entities.User
            {
                TelegramUserId = parsed.TelegramUserId,
                TelegramUsername = parsed.Username,
                DisplayName = parsed.DisplayName,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else if (parsed.Username is not null || parsed.DisplayName is not null)
        {
            user.TelegramUsername = parsed.Username ?? user.TelegramUsername;
            user.DisplayName = parsed.DisplayName ?? user.DisplayName;
            await _db.SaveChangesAsync(ct);
        }

        return new TelegramAuthResult(user.TelegramUserId, user.TelegramUsername, user.DisplayName);
    }
}
