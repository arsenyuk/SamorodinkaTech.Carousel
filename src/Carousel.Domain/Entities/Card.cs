namespace Carousel.Domain.Entities;

public class Card
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicationId { get; set; }
    public int Order { get; set; }
    public string? Text { get; set; }
    public Guid? MediaAssetId { get; set; }
    public CardStyle[]? Styles { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Publication Publication { get; set; } = null!;
    public MediaAsset? MediaAsset { get; set; }
}
