using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // Глобальная статистика (все тенанты) — только для Admin:Usernames;
        // ForbiddenException → 403 + ACCESS_DENIED (GlobalExceptionHandler).
        api.MapGet("/admin/stats", async (
            int? days,
            CarouselDbContext db,
            ICurrentUserContext context,
            Carousel.Application.Services.AdminOptions adminOptions,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            if (!adminOptions.IsAdmin(user.TelegramUsername))
            {
                throw new ForbiddenException("Admin access required.");
            }

            var window = Math.Clamp(days ?? 30, 1, 365);
            var from = DateTime.UtcNow.Date.AddDays(-(window - 1));

            // Глобальные запросы без VisibleTenantIds-фильтра; группировка в памяти (окно ≤365 дней).
            var publications = await db.Publications.AsNoTracking()
                .Where(p => p.CreatedAtUtc >= from)
                .Select(p => new
                {
                    p.CreatedAtUtc,
                    p.TemplateId,
                    p.NoAds,
                    CardCount = p.Cards.Count,
                    OwnImages = p.Cards.Any(c => c.MediaAssetId != null),
                })
                .ToListAsync(ct);

            // Только Completed: возврат (Refunded) не считаем как поступление.
            var payments = await db.Payments.AsNoTracking()
                .Where(p => p.Currency == "XTR"
                    && p.Status == PaymentStatus.Completed
                    && p.CreatedAtUtc >= from)
                .Select(p => new { p.CreatedAtUtc, p.AmountMinor })
                .ToListAsync(ct);

            var templateNames = await db.CarouselTemplates.AsNoTracking()
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            var pubByDay = publications
                .GroupBy(p => p.CreatedAtUtc.Date)
                .ToDictionary(g => g.Key, g => g.Count());
            var starsByDay = payments
                .GroupBy(p => p.CreatedAtUtc.Date)
                .ToDictionary(g => g.Key, g => new { Amount = g.Sum(x => x.AmountMinor), Count = g.Count() });

            var today = DateTime.UtcNow.Date;
            var dayStats = new List<AdminDayDto>(window);
            for (var d = from; d <= today; d = d.AddDays(1))
            {
                pubByDay.TryGetValue(d, out var pubCount);
                starsByDay.TryGetValue(d, out var stars);
                dayStats.Add(new AdminDayDto(
                    d.ToString("yyyy-MM-dd"),
                    pubCount,
                    stars?.Amount ?? 0,
                    stars?.Count ?? 0));
            }

            var byTemplate = publications
                .GroupBy(p => p.TemplateId)
                .Select(g => new AdminTemplateStat(
                    g.Key is null
                        ? "без шаблона"
                        : templateNames.TryGetValue(g.Key.Value, out var name) ? name : "удалённый шаблон",
                    g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();

            var cardGroups = publications
                .GroupBy(p => p.CardCount >= 6 ? "6+" : p.CardCount.ToString())
                .ToDictionary(g => g.Key, g => g.Count());
            var byCardCount = new[] { "0", "1", "2", "3", "4", "5", "6+" }
                .Select(bucket => new AdminBucketStat(bucket, cardGroups.GetValueOrDefault(bucket, 0)))
                .ToList();

            var counters = new AdminCounters(
                WithBotAds: publications.Count(p => !p.NoAds),
                NoAds: publications.Count(p => p.NoAds),
                WithOwnImages: publications.Count(p => p.OwnImages));

            var totals = new AdminStatsTotals(
                publications.Count,
                payments.Sum(p => p.AmountMinor),
                payments.Count);

            return Results.Ok(new AdminStatsDto(
                window,
                from,
                totals,
                dayStats,
                byTemplate,
                byCardCount,
                counters));
        });

        return app;
    }
}