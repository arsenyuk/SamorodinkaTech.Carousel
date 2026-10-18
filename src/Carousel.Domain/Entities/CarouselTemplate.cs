namespace Carousel.Domain.Entities;

public class CarouselTemplate
{
    public static readonly Guid SystemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = Guid.NewGuid();
    // null = global seed template (visible to all tenants).
    public Guid? TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DefaultStylesJson { get; set; }
    public Guid? BackgroundMediaAssetId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public MediaAsset? BackgroundMediaAsset { get; set; }
}
