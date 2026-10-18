namespace Carousel.Domain.Entities;

public class UsageCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Period { get; set; } = string.Empty; // YYYY-MM
    public string Metric { get; set; } = string.Empty;
    public long Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
