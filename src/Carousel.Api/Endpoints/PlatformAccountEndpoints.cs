using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Api.Endpoints;

internal static class PlatformAccountEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/platform-accounts", async (
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var accounts = await db.PlatformAccounts.AsNoTracking()
                .Where(a => tenantIds.Contains(a.TenantId))
                .OrderBy(a => a.CreatedAtUtc)
                .ToListAsync(ct);

            return Results.Ok(accounts.Select(a => new PlatformAccountDto(a.Id, a.Platform.ToString(), a.ExternalId, a.DisplayName, a.Status.ToString())).ToList());
        });

        api.MapPost("/platform-accounts", async (
            ConnectPlatformAccountRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IValidator<ConnectPlatformAccountRequest> validator,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            await validator.ValidateAndThrowAsync(request, ct);
            var tenantId = await context.ResolveTenantIdAsync(access, user.UserId, ct);
            await context.RequireMembershipAsync(access, tenantId, MemberRole.Owner, ct);

            var platform = request.Platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase)
                ? Platform.Instagram
                : Platform.Telegram;

            var account = new PlatformAccount
            {
                TenantId = tenantId,
                Platform = platform,
                ExternalId = request.ExternalId,
                DisplayName = request.DisplayName,
            };
            db.PlatformAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "PLATFORM:CONNECTED",
                TenantId: tenantId,
                UserId: user.UserId,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { account.Id, account.Platform }), ct);

            return Results.Created($"/api/platform-accounts/{account.Id}",
                new PlatformAccountDto(account.Id, account.Platform.ToString(), account.ExternalId, account.DisplayName, account.Status.ToString()));
        });

        api.MapDelete("/platform-accounts/{id:guid}", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            // L2: filter by visible tenants — cross-tenant resource returns 404, not 403.
            var tenantIds = await context.VisibleTenantIdsAsync(access, user.UserId, ct);
            var account = await db.PlatformAccounts
                .FirstOrDefaultAsync(a => a.Id == id && tenantIds.Contains(a.TenantId), ct)
                ?? throw new NotFoundException("Platform account not found.");

            await context.RequireMembershipAsync(access, account.TenantId, MemberRole.Owner, ct);

            db.PlatformAccounts.Remove(account);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "PLATFORM:DISCONNECTED",
                TenantId: account.TenantId,
                UserId: user.UserId,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { account.Id }), ct);
            return Results.NoContent();
        });

        return app;
    }
}
