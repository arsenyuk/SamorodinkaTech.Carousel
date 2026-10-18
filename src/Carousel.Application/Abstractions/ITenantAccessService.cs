using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public sealed record TenantMembership(Guid TenantId, MemberRole Role, Guid MemberId);

public interface ITenantAccessService
{
    Task<IReadOnlyList<TenantMembership>> GetMembershipsAsync(Guid userId, CancellationToken ct = default);
    Task<TenantMembership?> GetMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task EnsureMemberAsync(Guid userId, Guid tenantId, MemberRole minimumRole, CancellationToken ct = default);
    Task<TenantMembership> AddMemberAsync(Guid tenantId, Guid userId, MemberRole role, CancellationToken ct = default);
    Task UpdateMemberRoleAsync(Guid tenantId, Guid memberId, MemberRole role, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid tenantId, Guid memberId, CancellationToken ct = default);
}
