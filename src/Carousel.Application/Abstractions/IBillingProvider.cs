using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public interface IBillingProvider
{
    BillingProvider Provider { get; }
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default);
}

public sealed record CheckoutRequest(Guid TenantId, Guid PlanId, BillingProvider Channel);
public sealed record CheckoutResult(string CheckoutId, string? RedirectUrl, long AmountMinor, string Currency);
