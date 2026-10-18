using Carousel.Application.Abstractions;
using Carousel.Application.Security;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Auth;

public sealed class TenantAccessService : ITenantAccessService
{
    private readonly CarouselDbContext _db;

    public TenantAccessService(CarouselDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TenantMembership>> GetMembershipsAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.TenantMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new TenantMembership(m.TenantId, m.Role, m.Id))
            .ToListAsync(ct);
    }

    public async Task<TenantMembership?> GetMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var member = await _db.TenantMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);

        return member is null ? null : new TenantMembership(member.TenantId, member.Role, member.Id);
    }

    public async Task EnsureMemberAsync(Guid userId, Guid tenantId, MemberRole minimumRole, CancellationToken ct = default)
    {
        var membership = await GetMembershipAsync(userId, tenantId, ct)
            ?? throw new ForbiddenException("You are not a member of this workspace.");

        if (membership.Role > minimumRole)
        {
            // Lower enum value = higher privilege (Owner=0, Editor=1, Viewer=2)
            throw new ForbiddenException($"This action requires {minimumRole} role or higher.");
        }
    }

    public async Task<TenantMembership> AddMemberAsync(Guid tenantId, Guid userId, MemberRole role, CancellationToken ct = default)
    {
        var exists = await _db.TenantMembers.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
        if (exists)
        {
            throw new ConflictException("User is already a member of this workspace.");
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        var member = new Domain.Entities.TenantMember
        {
            TenantId = tenantId,
            UserId = user.Id,
            Role = role,
        };
        _db.TenantMembers.Add(member);
        await _db.SaveChangesAsync(ct);

        return new TenantMembership(member.TenantId, member.Role, member.Id);
    }

    public async Task UpdateMemberRoleAsync(Guid tenantId, Guid memberId, MemberRole role, CancellationToken ct = default)
    {
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId, ct)
            ?? throw new NotFoundException("Member not found.");

        if (member.Role == MemberRole.Owner)
        {
            throw new ForbiddenException("Cannot change Owner role.");
        }

        member.Role = role;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid tenantId, Guid memberId, CancellationToken ct = default)
    {
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId, ct)
            ?? throw new NotFoundException("Member not found.");

        if (member.Role == MemberRole.Owner)
        {
            throw new ForbiddenException("Cannot remove Owner.");
        }

        _db.TenantMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
    }
}
