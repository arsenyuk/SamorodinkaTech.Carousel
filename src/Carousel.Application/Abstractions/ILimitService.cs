using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public interface ILimitService
{
    Task EnsureCanPublishAsync(Guid tenantId, int cardCount, CancellationToken ct = default);
    Task EnsureImageAllowedAsync(Guid tenantId, long sizeBytes, int width, int height, CancellationToken ct = default);
    Task IncrementPublicationsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// H3: atomically reserves one monthly publication slot (check + increment in one SQL).
    /// Throws LimitExceededException if the limit is already reached.
    /// Caller must call ReleasePublicationAsync if delivery fails entirely.
    /// </summary>
    Task ReservePublicationAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>H3 compensation: release a previously reserved slot when publish fails.</summary>
    Task ReleasePublicationAsync(Guid tenantId, CancellationToken ct = default);

    Task<long?> GetLimitAsync(Guid tenantId, LimitKey key, CancellationToken ct = default);
    Task<IReadOnlyDictionary<LimitKey, long>> GetEffectiveLimitsAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class LimitExceededException : Exception
{
    public LimitExceededException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionRequiredException : Exception
{
    public SubscriptionRequiredException(string message) : base(message)
    {
    }
}
