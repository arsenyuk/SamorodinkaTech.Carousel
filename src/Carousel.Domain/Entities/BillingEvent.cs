namespace Carousel.Domain.Entities;

public class BillingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PaymentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;

    public Payment? Payment { get; set; }
}
