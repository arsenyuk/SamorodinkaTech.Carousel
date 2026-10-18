using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Carousel.Infrastructure.Auth;

/// <summary>
/// Resolves current user from Bearer JWT (Stage 3) or Development-only X-Telegram-User-Id header.
/// Legacy raw user-Guid tokens are no longer accepted.
/// </summary>
public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _http;
    private readonly CarouselDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IJwtTokenService _jwt;

    public CurrentUserContext(IHttpContextAccessor http, CarouselDbContext db, IWebHostEnvironment env, IJwtTokenService jwt)
    {
        _http = http;
        _db = db;
        _env = env;
        _jwt = jwt;
    }

    private HttpContext? HttpContext => _http.HttpContext;

    public bool IsDevelopment => _env.IsDevelopment();

    public Guid? TenantId
    {
        get
        {
            var header = HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            return header is not null && Guid.TryParse(header, out var tenantId) ? tenantId : null;
        }
    }

    public async Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var ctx = HttpContext;
        if (ctx is null)
        {
            return null;
        }

        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..];
            var userId = await _jwt.ValidateAndGetUserIdAsync(token, ct);
            if (userId is not null)
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
                if (user is not null)
                {
                    return new CurrentUser(user.Id, user.TelegramUserId, user.TelegramUsername, user.DisplayName);
                }
            }

            return null;
        }

        if (_env.IsDevelopment())
        {
            var header = ctx.Request.Headers["X-Telegram-User-Id"].FirstOrDefault();
            if (header is not null && long.TryParse(header, out var telegramId))
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == telegramId, ct);
                if (user is not null)
                {
                    return new CurrentUser(user.Id, user.TelegramUserId, user.TelegramUsername, user.DisplayName);
                }
            }
        }

        return null;
    }
}
