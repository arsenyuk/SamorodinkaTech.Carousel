namespace Carousel.Domain.Entities;

public class PlatformCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlatformAccountId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public byte[] EncryptedPayload { get; set; } = [];
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformAccount PlatformAccount { get; set; } = null!;
}
