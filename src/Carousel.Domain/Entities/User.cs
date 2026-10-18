namespace Carousel.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Tenant> OwnedTenants { get; set; } = [];
    public ICollection<TenantMember> Memberships { get; set; } = [];
}
