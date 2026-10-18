using Carousel.Application.Abstractions;
using Carousel.Domain.Enums;

namespace Carousel.Infrastructure.Billing;

public interface ITelegramStarsBillingProvider : IBillingProvider
{
}

public interface IWebPaymentProvider : IBillingProvider
{
}

/// <summary>Stage 1 stub for Telegram Stars checkout.</summary>
public sealed class StubStarsBillingProvider : ITelegramStarsBillingProvider
{
    public BillingProvider Provider => BillingProvider.TelegramStars;

    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var id = $"stars-{Guid.NewGuid():N}";
        return Task.FromResult(new CheckoutResult(id, null, 500, "XTR"));
    }
}

/// <summary>Stage 1 stub for web payment provider.</summary>
public sealed class StubWebBillingProvider : IWebPaymentProvider
{
    public BillingProvider Provider => BillingProvider.Web;

    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var id = $"web-{Guid.NewGuid():N}";
        return Task.FromResult(new CheckoutResult(id, "https://example.invalid/checkout/" + id, 500, "USD"));
    }
}
