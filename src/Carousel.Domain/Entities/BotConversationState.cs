using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public class BotConversationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserId { get; set; }
    public Guid? TenantId { get; set; }
    public ConversationStep Step { get; set; } = ConversationStep.None;
    public Guid? DraftPublicationId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
