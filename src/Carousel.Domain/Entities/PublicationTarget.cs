using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public class PublicationTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicationId { get; set; }
    public Platform Platform { get; set; }
    public Guid PlatformAccountId { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Publication Publication { get; set; } = null!;
    public PlatformAccount PlatformAccount { get; set; } = null!;
    public ICollection<PublicationDelivery> Deliveries { get; set; } = [];
}
