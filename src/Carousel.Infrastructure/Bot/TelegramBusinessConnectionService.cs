using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Bot;

/// <summary>
/// Persists Telegram business_connection updates and answers the publish flow
/// with the connection id used by postStory.
/// </summary>
public sealed class TelegramBusinessConnectionService : ITelegramBusinessConnectionStore
{
    private readonly CarouselDbContext _db;

    public TelegramBusinessConnectionService(CarouselDbContext db)
    {
        _db = db;
    }

    public async Task<TelegramBusinessConnection?> GetEnabledConnectionAsync(
        long telegramUserId, CancellationToken ct = default)
    {
        var row = await _db.BusinessConnections.AsNoTracking()
            .Where(c => c.TelegramUserId == telegramUserId && c.IsEnabled)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new TelegramBusinessConnection(row.ConnectionId, row.TelegramUserId, row.Username, row.IsEnabled);
    }

    public async Task UpsertAsync(TelegramBusinessConnection connection, CancellationToken ct = default)
    {
        var row = await _db.BusinessConnections
            .FirstOrDefaultAsync(c => c.ConnectionId == connection.Id, ct);

        if (row is null)
        {
            row = new Domain.Entities.BusinessConnection
            {
                ConnectionId = connection.Id,
                TelegramUserId = connection.UserId,
                Username = connection.Username,
                IsEnabled = connection.IsEnabled,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _db.BusinessConnections.Add(row);
        }
        else
        {
            row.TelegramUserId = connection.UserId;
            row.Username = connection.Username;
            row.IsEnabled = connection.IsEnabled;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}