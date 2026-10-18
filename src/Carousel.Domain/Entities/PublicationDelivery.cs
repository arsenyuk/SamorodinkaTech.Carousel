using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public class PublicationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicationTargetId { get; set; }
    public int Attempt { get; set; }
    public string? ExternalPostId { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public string? Error { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PublicationTarget PublicationTarget { get; set; } = null!;
}
