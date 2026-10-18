using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Bot;

public sealed class BotConversationEngine : IBotConversationEngine
{
    // M6: cap payload size to prevent DB bloat / memory pressure from untrusted bot input.
    private const int MaxPayloadJsonBytes = 32 * 1024;

    private readonly CarouselDbContext _db;

    public BotConversationEngine(CarouselDbContext db)
    {
        _db = db;
    }

    public async Task<ConversationSnapshot> GetStateAsync(long telegramUserId, CancellationToken ct = default)
    {
        var state = await _db.BotConversationStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == telegramUserId, ct);

        return state is null
            ? new ConversationSnapshot(Domain.Enums.ConversationStep.None, null, null, "{}")
            : new ConversationSnapshot(state.Step, state.TenantId, state.DraftPublicationId, state.PayloadJson);
    }

    public async Task<ConversationSnapshot> AdvanceAsync(
        long telegramUserId,
        Domain.Enums.ConversationStep step,
        Guid? tenantId,
        Guid? draftPublicationId,
        string? payloadJson,
        CancellationToken ct = default)
    {
        // M6: reject oversized payloads before they hit the DB.
        if (payloadJson is not null && System.Text.Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadJsonBytes)
        {
            throw new Carousel.Application.Security.ValidationException(
                $"Payload exceeds {MaxPayloadJsonBytes / 1024} KB limit.");
        }

        var state = await _db.BotConversationStates.FirstOrDefaultAsync(s => s.UserId == telegramUserId, ct);
        if (state is null)
        {
            state = new Domain.Entities.BotConversationState { UserId = telegramUserId };
            _db.BotConversationStates.Add(state);
        }

        state.Step = step;
        state.TenantId = tenantId ?? state.TenantId;
        state.DraftPublicationId = draftPublicationId ?? state.DraftPublicationId;
        state.PayloadJson = payloadJson is null ? state.PayloadJson : payloadJson;
        state.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new ConversationSnapshot(state.Step, state.TenantId, state.DraftPublicationId, state.PayloadJson);
    }

    public async Task ClearAsync(long telegramUserId, CancellationToken ct = default)
    {
        var state = await _db.BotConversationStates.FirstOrDefaultAsync(s => s.UserId == telegramUserId, ct);
        if (state is not null)
        {
            _db.BotConversationStates.Remove(state);
            await _db.SaveChangesAsync(ct);
        }
    }
}
