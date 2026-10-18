using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public class Publication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public Guid? TemplateId { get; set; }

    /// <summary>Крыжек «Отключить рекламу бота» (разовый, на эту публикацию).</summary>
    public bool NoAds { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public CarouselTemplate? Template { get; set; }
    public ICollection<Card> Cards { get; set; } = [];
    public ICollection<PublicationTarget> Targets { get; set; } = [];
}
