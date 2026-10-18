namespace Carousel.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public User Owner { get; set; } = null!;
    public ICollection<TenantMember> Members { get; set; } = [];
    public ICollection<Publication> Publications { get; set; } = [];
}
