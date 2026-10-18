using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Infrastructure.Persistence;

namespace Carousel.Api.Endpoints;

internal static class TelegramChatEndpoints
{
    public static IEndpointRouteBuilder MapTelegramChatEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/telegram");

        // Mini App opened from a group/channel: check whether current user is chat admin.
        api.MapGet("/chat-access", async (
            long chatId,
            ICurrentUserContext context,
            ITelegramBotApi bot,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);

            if (chatId == 0)
            {
                throw new ValidationException("chatId is required.");
            }

            var member = await bot.GetChatMemberAsync(chatId, user.TelegramUserId, ct);
            if (member is null)
            {
                return Results.Ok(new ChatAccessDto(chatId, false, false, "unknown", false));
            }

            var isMember = member.Status is not ("left" or "kicked");
            return Results.Ok(new ChatAccessDto(
                chatId,
                isMember,
                member.IsAdmin,
                member.Status,
                member.IsAdmin));
        });

        // Username бота для ссылок в Mini App (оверрайд конфига → getMe → mockbot).
        // Публично: имя бота и так не секрет.
        api.MapGet("/bot-username", async (ITelegramBotApi bot, CancellationToken ct) =>
        {
            var username = await bot.GetBotUsernameAsync(ct);
            return Results.Ok(new { username });
        });

        // Story publishing requires the user's business connection (Настройки → Бизнес → бот).
        api.MapGet("/business-connection", async (
            ICurrentUserContext context,
            ITelegramBusinessConnectionStore store,
            CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            var connection = await store.GetEnabledConnectionAsync(user.TelegramUserId, ct);
            return Results.Ok(new { connected = connection is not null });
        });

        return app;
    }
}

internal sealed record ChatAccessDto(
    long ChatId,
    bool IsMember,
    bool IsAdmin,
    string Status,
    bool CanPublish);
