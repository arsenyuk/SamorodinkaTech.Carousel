using Carousel.Application.Abstractions;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ValidationException = Carousel.Application.Security.ValidationException;

namespace Carousel.Api.Endpoints;

internal static class EndpointHelpers
{
    public static async Task<CurrentUser> RequireUserAsync(this ICurrentUserContext context, CancellationToken ct)
    {
        // L1: generic 401 — do not advertise debug headers (X-Telegram-User-Id) to clients.
        return await context.GetCurrentUserAsync(ct)
            ?? throw new UnauthorizedException("Not authenticated.");
    }

    public static async Task<Guid> ResolveTenantIdAsync(
        this ICurrentUserContext context,
        ITenantAccessService access,
        Guid userId,
        CancellationToken ct)
    {
        var memberships = await access.GetMembershipsAsync(userId, ct);
        if (memberships.Count == 0)
        {
            throw new ForbiddenException("User has no workspace. Create one via POST /api/tenants.");
        }

        var header = context.TenantId;
        if (header is null)
        {
            throw new ValidationException("X-Tenant-Id header is required.");
        }

        if (!memberships.Any(m => m.TenantId == header.Value))
        {
            throw new ForbiddenException("You are not a member of this workspace.");
        }

        return header.Value;
    }

    public static async Task<(CurrentUser User, TenantMembership Membership)> RequireMembershipAsync(
        this ICurrentUserContext context,
        ITenantAccessService access,
        Guid tenantId,
        MemberRole minimumRole,
        CancellationToken ct)
    {
        var user = await context.RequireUserAsync(ct);
        var membership = await access.GetMembershipAsync(user.UserId, tenantId, ct)
            ?? throw new ForbiddenException("You are not a member of this workspace.");

        // Owner=0 highest; minimumRole is the least privileged role allowed.
        if (membership.Role > minimumRole)
        {
            throw new ForbiddenException($"This action requires {minimumRole} role or higher.");
        }

        return (user, membership);
    }

    // L2: filter by visible tenants — cross-tenant resource returns 404, not 403.
    // Prevents existence oracle: attacker cannot distinguish "exists but not yours" from "doesn't exist".
    public static async Task<Publication> FindPublicationAsync(
        CarouselDbContext db,
        IReadOnlyList<Guid> visibleTenantIds,
        Guid id,
        CancellationToken ct)
    {
        var publication = await db.Publications
            .Include(p => p.Cards)
            .ThenInclude(c => c.MediaAsset)
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == id && visibleTenantIds.Contains(p.TenantId), ct);

        if (publication is null)
        {
            throw new NotFoundException("Publication not found.");
        }

        return publication;
    }

    public static async Task EnsurePublicationAccessAsync(
        ITenantAccessService access,
        Guid userId,
        Guid tenantId,
        MemberRole minimumRole,
        CancellationToken ct)
    {
        await access.EnsureMemberAsync(userId, tenantId, minimumRole, ct);
    }

    public static async Task<List<Guid>> VisibleTenantIdsAsync(
        this ICurrentUserContext context,
        ITenantAccessService access,
        Guid userId,
        CancellationToken ct)
    {
        var memberships = await access.GetMembershipsAsync(userId, ct);
        return memberships.Select(m => m.TenantId).ToList();
    }
}
