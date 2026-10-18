using Carousel.Application.Abstractions;
using Carousel.Domain.Enums;

namespace Carousel.Application.Services;

/// <summary>
/// Minimal read model used by LimitService to avoid taking a dependency on EF Core DbContext type.
/// Infrastructure supplies an implementation backed by EF.
/// </summary>
public interface IBillingQuery
{
    Task<SubscriptionSnapshot?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<LimitKey, long>> GetPlanLimitsAsync(Guid planId, CancellationToken ct = default);
    Task<long> GetMonthlyPublicationCountAsync(Guid tenantId, string period, CancellationToken ct = default);
    Task IncrementMonthlyPublicationsAsync(Guid tenantId, string period, CancellationToken ct = default);

    /// <summary>
    /// H3: atomically checks used &lt; maxMonthly and increments in one SQL statement.
    /// Returns true if reservation succeeded; false if limit already reached.
    /// </summary>
    Task<bool> TryReserveMonthlyPublicationAsync(Guid tenantId, string period, long maxMonthly, CancellationToken ct = default);

    /// <summary>H3 compensation: decrement counter when a reserved publish fails entirely.</summary>
    Task DecrementMonthlyPublicationsAsync(Guid tenantId, string period, CancellationToken ct = default);
}

public sealed record SubscriptionSnapshot(Guid SubscriptionId, Guid PlanId, SubscriptionStatus Status, DateTime PeriodEndUtc);

public sealed class LimitService : ILimitService
{
    // Fallbacks when tenant has no active subscription (media validation should still work for draft uploads if product decides).
    // Stage 2 rule: free-tier defaults when no subscription row exists yet.
    private static readonly IReadOnlyDictionary<LimitKey, long> FreeDefaults = new Dictionary<LimitKey, long>
    {
        [LimitKey.MaxImageSizeMb] = 5,
        [LimitKey.MaxImageDimensionPx] = 2048,
        [LimitKey.CardsPerPublication] = 5,
        [LimitKey.MonthlyPublications] = 3,
    };

    private readonly IBillingQuery _billing;

    public LimitService(IBillingQuery billing)
    {
        _billing = billing;
    }

    public async Task EnsureCanPublishAsync(Guid tenantId, int cardCount, CancellationToken ct = default)
    {
        var subscription = await _billing.GetActiveSubscriptionAsync(tenantId, ct);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active || subscription.PeriodEndUtc < DateTime.UtcNow)
        {
            throw new SubscriptionRequiredException("Active subscription is required to publish.");
        }

        var limits = await _billing.GetPlanLimitsAsync(subscription.PlanId, ct);

        if (limits.TryGetValue(LimitKey.CardsPerPublication, out var maxCards) && cardCount > maxCards)
        {
            throw new LimitExceededException($"Publication has {cardCount} cards; plan allows {maxCards}.");
        }

        if (limits.TryGetValue(LimitKey.MonthlyPublications, out var maxMonthly))
        {
            var period = DateTime.UtcNow.ToString("yyyy-MM");
            var used = await _billing.GetMonthlyPublicationCountAsync(tenantId, period, ct);
            if (used >= maxMonthly)
            {
                throw new LimitExceededException($"Monthly publication limit reached ({maxMonthly}).");
            }
        }
    }

    public async Task EnsureImageAllowedAsync(Guid tenantId, long sizeBytes, int width, int height, CancellationToken ct = default)
    {
        var limits = await GetEffectiveLimitsAsync(tenantId, ct);

        if (limits.TryGetValue(LimitKey.MaxImageSizeMb, out var maxMb))
        {
            var maxBytes = maxMb * 1024L * 1024L;
            if (sizeBytes > maxBytes)
            {
                throw new LimitExceededException($"Image size {sizeBytes} bytes exceeds plan limit {maxMb} MB.");
            }
        }

        if (limits.TryGetValue(LimitKey.MaxImageDimensionPx, out var maxPx))
        {
            if (width > maxPx || height > maxPx)
            {
                throw new LimitExceededException($"Image dimensions {width}x{height} exceed plan limit {maxPx}px.");
            }
        }
    }

    public async Task IncrementPublicationsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var period = DateTime.UtcNow.ToString("yyyy-MM");
        await _billing.IncrementMonthlyPublicationsAsync(tenantId, period, ct);
    }

    public async Task ReservePublicationAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _billing.GetActiveSubscriptionAsync(tenantId, ct);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active || subscription.PeriodEndUtc < DateTime.UtcNow)
        {
            throw new SubscriptionRequiredException("Active subscription is required to publish.");
        }

        var limits = await _billing.GetPlanLimitsAsync(subscription.PlanId, ct);
        if (!limits.TryGetValue(LimitKey.MonthlyPublications, out var maxMonthly) || maxMonthly <= 0)
        {
            // No monthly limit configured — skip reservation.
            return;
        }

        var period = DateTime.UtcNow.ToString("yyyy-MM");
        var reserved = await _billing.TryReserveMonthlyPublicationAsync(tenantId, period, maxMonthly, ct);
        if (!reserved)
        {
            throw new LimitExceededException($"Monthly publication limit reached ({maxMonthly}).");
        }
    }

    public async Task ReleasePublicationAsync(Guid tenantId, CancellationToken ct = default)
    {
        var period = DateTime.UtcNow.ToString("yyyy-MM");
        await _billing.DecrementMonthlyPublicationsAsync(tenantId, period, ct);
    }

    public async Task<long?> GetLimitAsync(Guid tenantId, LimitKey key, CancellationToken ct = default)
    {
        var limits = await GetEffectiveLimitsAsync(tenantId, ct);
        return limits.TryGetValue(key, out var value) ? value : null;
    }

    public async Task<IReadOnlyDictionary<LimitKey, long>> GetEffectiveLimitsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _billing.GetActiveSubscriptionAsync(tenantId, ct);
        if (subscription is null || subscription.PeriodEndUtc < DateTime.UtcNow)
        {
            return FreeDefaults;
        }

        var limits = await _billing.GetPlanLimitsAsync(subscription.PlanId, ct);
        return limits.Count == 0 ? FreeDefaults : limits;
    }
}
