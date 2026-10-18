using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public interface IBotConversationEngine
{
    Task<ConversationSnapshot> GetStateAsync(long telegramUserId, CancellationToken ct = default);
    Task<ConversationSnapshot> AdvanceAsync(long telegramUserId, ConversationStep step, Guid? tenantId, Guid? draftPublicationId, string? payloadJson, CancellationToken ct = default);
    Task ClearAsync(long telegramUserId, CancellationToken ct = default);
}

public sealed record ConversationSnapshot(ConversationStep Step, Guid? TenantId, Guid? DraftPublicationId, string PayloadJson);
