namespace Carousel.Domain.Entities;

public class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>PostgreSQL bytea payload (Stage 1). Replaceable by S3 via IMediaStorage.</summary>
    public byte[]? Content { get; set; }
}
