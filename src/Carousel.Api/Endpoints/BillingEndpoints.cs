using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Billing;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Api.Endpoints;

internal static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/billing/plans", async (CarouselDbContext db, CancellationToken ct) =>
        {
            var plans = await db.Plans.AsNoTracking()
                .Include(p => p.Limits)
                .Where(p => p.IsActive)
                .OrderBy(p => p.PriceMinor)
                .ToListAsync(ct);

            return Results.Ok(plans.Select(p => new PlanDto(
                p.Id,
                p.Code,
                p.Name,
                p.PriceMinor,
                p.Currency,
                p.Limits.ToDictionary(l => l.LimitKey.ToString(), l => l.Value))).ToList());
        });

        // Цена публикации в Stars для кнопки/подсказок Mini App (публично — сумма не секрет).
        api.MapGet("/billing/stars-config", (Carousel.Application.Services.StarsBillingOptions options) =>
            Results.Ok(new { price = options.StarsPrice }));

        api.MapGet("/billing/subscription", async (
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            Application.Services.IBillingQuery billing,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            var snapshot = await billing.GetActiveSubscriptionAsync(tenantId, ct);

            if (snapshot is null)
            {
                return Results.Ok(new SubscriptionDto(Guid.Empty, Guid.Empty, string.Empty, SubscriptionStatus.None.ToString(), DateTime.MinValue, DateTime.MinValue));
            }

            var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == snapshot.PlanId, ct);
            return Results.Ok(new SubscriptionDto(snapshot.SubscriptionId, snapshot.PlanId, plan.Code, snapshot.Status.ToString(), snapshot.PeriodEndUtc.AddDays(-30), snapshot.PeriodEndUtc));
        });

        api.MapPost("/billing/checkout", async (
            CheckoutRequestDto request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IWebPaymentProvider webProvider,
            ITelegramStarsBillingProvider starsProvider,
            IValidator<CheckoutRequestDto> validator,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            await validator.ValidateAndThrowAsync(request, ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Owner, ct);

            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive, ct)
                ?? throw new NotFoundException("Plan not found.");

            var provider = request.Provider.Equals("TelegramStars", StringComparison.OrdinalIgnoreCase)
                ? (IBillingProvider)starsProvider
                : webProvider;

            var checkout = await provider.CreateCheckoutAsync(new CheckoutRequest(tenantId, plan.Id, provider.Provider), ct);

            var payment = new Payment
            {
                TenantId = tenantId,
                Provider = provider.Provider,
                ExternalId = checkout.CheckoutId,
                AmountMinor = checkout.AmountMinor,
                Currency = checkout.Currency,
                IdempotencyKey = checkout.CheckoutId,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new CheckoutResponseDto(checkout.CheckoutId, checkout.RedirectUrl, checkout.AmountMinor, checkout.Currency));
        });

        // --- Webhooks (stubs) ---
        // C3 fix: reject unknown providers; gate behind Billing:WebhooksEnabled config.
        // TODO(Stage 5): add Telegram Stars / provider signature verification + replay protection before enabling real billing.
        api.MapPost("/webhooks/telegram-stars", (IConfiguration config) =>
            config.GetValue<bool>("Billing:WebhooksEnabled")
                ? Results.Accepted()
                : Results.NotFound());

        api.MapPost("/webhooks/payments/{provider}", (string provider, IConfiguration config, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("BillingWebhooks");
            if (!config.GetValue<bool>("Billing:WebhooksEnabled"))
            {
                return Results.NotFound();
            }

            if (provider is not ("TelegramStars" or "Web" or "telegramstars" or "web"))
            {
                logger.LogWarning(
                    "Validation failed for {Path}: unknown payment provider {Provider}",
                    $"/api/billing/webhooks/payments/{provider}",
                    provider);
                return Results.BadRequest("Unknown payment provider.");
            }

            return Results.Accepted();
        });

        api.MapPost("/webhooks/telegram/bot", (IConfiguration config) =>
            config.GetValue<bool>("Billing:WebhooksEnabled")
                ? Results.Accepted()
                : Results.NotFound());

        return app;
    }
}
