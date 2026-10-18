using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Api.Endpoints;

internal static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/tenants", async (
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var memberships = await access.GetMembershipsAsync(user.UserId, ct);
            var tenantIds = memberships.Select(m => m.TenantId).ToList();

            var tenants = await db.Tenants.AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id))
                .OrderBy(t => t.CreatedAtUtc)
                .Select(t => new TenantDto(t.Id, t.Name, t.OwnerUserId, t.CreatedAtUtc))
                .ToListAsync(ct);

            return Results.Ok(tenants);
        });

        api.MapPost("/tenants", async (
            ICurrentUserContext context,
            CreateTenantRequest request,
            CarouselDbContext db,
            IValidator<CreateTenantRequest> validator,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            await validator.ValidateAndThrowAsync(request, ct);

            var tenant = new Tenant { Name = request.Name, OwnerUserId = user.UserId };
            db.Tenants.Add(tenant);
            db.TenantMembers.Add(new TenantMember
            {
                TenantId = tenant.Id,
                UserId = user.UserId,
                Role = MemberRole.Owner,
            });

            var free = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == "free" && p.IsActive, ct);
            if (free is not null)
            {
                db.Subscriptions.Add(new Subscription
                {
                    TenantId = tenant.Id,
                    PlanId = free.Id,
                    Status = SubscriptionStatus.Active,
                    PeriodStartUtc = DateTime.UtcNow,
                    PeriodEndUtc = DateTime.UtcNow.AddDays(30),
                });
            }

            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "TENANT:CREATED",
                TenantId: tenant.Id,
                UserId: user.UserId,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { tenant.Name }), ct);

            return Results.Created($"/api/tenants/{tenant.Id}", new TenantDto(tenant.Id, tenant.Name, tenant.OwnerUserId, tenant.CreatedAtUtc));
        });

        api.MapGet("/tenants/{id:guid}/members", async (
            Guid id,
            ICurrentUserContext context,
            ITenantAccessService access,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            await context.RequireMembershipAsync(access, id, MemberRole.Viewer, ct);
            var members = await db.TenantMembers.AsNoTracking()
                .Where(m => m.TenantId == id)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new TenantMemberDto(m.Id, u.Id, u.TelegramUserId, u.DisplayName, m.Role.ToString()))
                .ToListAsync(ct);

            return Results.Ok(members);
        });

        api.MapPost("/tenants/{id:guid}/members", async (
            Guid id,
            AddMemberRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            IValidator<AddMemberRequest> validator,
            IAuditService audit,
            CarouselDbContext db,
            CancellationToken ct) =>
        {
            var actor = await context.RequireMembershipAsync(access, id, MemberRole.Owner, ct);
            await validator.ValidateAndThrowAsync(request, ct);

            if (!Enum.TryParse<MemberRole>(request.Role, true, out var role) || role == MemberRole.Owner)
            {
                throw new Carousel.Application.Security.ValidationException("Invalid role.");
            }

            var targetUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == request.TelegramUserId, ct);
            if (targetUser is null)
            {
                throw new NotFoundException("User must authenticate via bot/API before being added.");
            }

            var membership = await access.AddMemberAsync(id, targetUser.Id, role, ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "TENANT:MEMBER_ADDED",
                TenantId: id,
                UserId: actor.User.UserId,
                Login: actor.User.TelegramUsername ?? actor.User.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { targetUser.TelegramUserId, Role = role.ToString() }), ct);

            return Results.Ok(new TenantMemberDto(membership.MemberId, targetUser.Id, targetUser.TelegramUserId, targetUser.DisplayName, role.ToString()));
        });

        api.MapPatch("/tenants/{id:guid}/members/{memberId:guid}", async (
            Guid id,
            Guid memberId,
            UpdateMemberRequest request,
            ICurrentUserContext context,
            ITenantAccessService access,
            IValidator<UpdateMemberRequest> validator,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var actor = await context.RequireMembershipAsync(access, id, MemberRole.Owner, ct);
            await validator.ValidateAndThrowAsync(request, ct);

            if (!Enum.TryParse<MemberRole>(request.Role, true, out var role) || role == MemberRole.Owner)
            {
                throw new Carousel.Application.Security.ValidationException("Invalid role.");
            }

            await access.UpdateMemberRoleAsync(id, memberId, role, ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "TENANT:MEMBER_ROLE_CHANGED",
                TenantId: id,
                UserId: actor.User.UserId,
                Login: actor.User.TelegramUsername ?? actor.User.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { memberId, Role = role.ToString() }), ct);
            return Results.NoContent();
        });

        api.MapDelete("/tenants/{id:guid}/members/{memberId:guid}", async (
            Guid id,
            Guid memberId,
            ICurrentUserContext context,
            ITenantAccessService access,
            IAuditService audit,
            CancellationToken ct) =>
        {
            var actor = await context.RequireMembershipAsync(access, id, MemberRole.Owner, ct);
            await access.RemoveMemberAsync(id, memberId, ct);
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "TENANT:MEMBER_REMOVED",
                TenantId: id,
                UserId: actor.User.UserId,
                Login: actor.User.TelegramUsername ?? actor.User.TelegramUserId.ToString(),
                Result: "SUCCESS",
                Details: new { memberId }), ct);
            return Results.NoContent();
        });

        return app;
    }
}
