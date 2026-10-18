using Carousel.Application.Services;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Billing;

public sealed class EfBillingQuery : IBillingQuery
{
    private readonly CarouselDbContext _db;

    public EfBillingQuery(CarouselDbContext db)
    {
        _db = db;
    }

    public async Task<SubscriptionSnapshot?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.PeriodEndUtc)
            .FirstOrDefaultAsync(ct);

        if (sub is null)
        {
            // Lazy free-plan bootstrap for tenants created before Stage 2 or without subscription.
            var free = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == "free" && p.IsActive, ct);
            if (free is null)
            {
                return null;
            }

            var created = new Subscription
            {
                TenantId = tenantId,
                PlanId = free.Id,
                Status = SubscriptionStatus.Active,
                PeriodStartUtc = DateTime.UtcNow,
                PeriodEndUtc = DateTime.UtcNow.AddDays(30),
            };
            _db.Subscriptions.Add(created);
            await _db.SaveChangesAsync(ct);

            return new SubscriptionSnapshot(created.Id, created.PlanId, created.Status, created.PeriodEndUtc);
        }

        return sub.PeriodEndUtc < DateTime.UtcNow
            ? null
            : new SubscriptionSnapshot(sub.Id, sub.PlanId, sub.Status, sub.PeriodEndUtc);
    }

    public async Task<IReadOnlyDictionary<LimitKey, long>> GetPlanLimitsAsync(Guid planId, CancellationToken ct = default)
    {
        var limits = await _db.PlanLimits.AsNoTracking()
            .Where(l => l.PlanId == planId)
            .ToListAsync(ct);

        return limits.ToDictionary(l => l.LimitKey, l => l.Value);
    }

    public async Task<long> GetMonthlyPublicationCountAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var counter = await _db.UsageCounters.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Period == period && u.Metric == "MonthlyPublications", ct);

        return counter?.Value ?? 0;
    }

    public async Task IncrementMonthlyPublicationsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        // Atomic upsert to avoid lost updates under concurrent publishes.
        // Column names match EF PascalCase mapping (no snake_case naming convention).
        // EF 10: ExecuteSqlAsync(FormattableString) replaces ExecuteSqlRawAsync.
        await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO usage_counters ("Id", "TenantId", "Period", "Metric", "Value", "UpdatedAtUtc")
            VALUES ({Guid.NewGuid()}, {tenantId}, {period}, 'MonthlyPublications', 1, now() at time zone 'utc')
            ON CONFLICT ("TenantId", "Period", "Metric")
            DO UPDATE SET "Value" = usage_counters."Value" + 1, "UpdatedAtUtc" = now() at time zone 'utc';
            """);
    }

    public async Task<bool> TryReserveMonthlyPublicationAsync(Guid tenantId, string period, long maxMonthly, CancellationToken ct = default)
    {
        // H3: atomic check-and-increment in one statement. Prevents TOCTOU race where
        // two concurrent publishes both pass EnsureCanPublishAsync before either increments.
        // INSERT when no row exists (always allowed if maxMonthly >= 1);
        // conditional UPDATE only when current Value < maxMonthly.
        var affected = await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO usage_counters ("Id", "TenantId", "Period", "Metric", "Value", "UpdatedAtUtc")
            VALUES ({Guid.NewGuid()}, {tenantId}, {period}, 'MonthlyPublications', 1, now() at time zone 'utc')
            ON CONFLICT ("TenantId", "Period", "Metric")
            DO UPDATE SET "Value" = usage_counters."Value" + 1, "UpdatedAtUtc" = now() at time zone 'utc'
            WHERE usage_counters."Value" < {maxMonthly};
            """);

        // affected > 0 means either inserted or updated. If the WHERE blocked the update
        // and no row was inserted (because row already existed at limit), affected == 0.
        return affected > 0;
    }

    public async Task DecrementMonthlyPublicationsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlAsync($"""
            UPDATE usage_counters
            SET "Value" = GREATEST("Value" - 1, 0), "UpdatedAtUtc" = now() at time zone 'utc'
            WHERE "TenantId" = {tenantId} AND "Period" = {period} AND "Metric" = 'MonthlyPublications';
            """);
    }
}
