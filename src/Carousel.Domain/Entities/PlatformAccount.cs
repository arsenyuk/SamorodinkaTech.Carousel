using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public class PlatformAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Platform Platform { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public PlatformAccountStatus Status { get; set; } = PlatformAccountStatus.Active;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<PlatformCredential> Credentials { get; set; } = [];
}
