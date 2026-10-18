using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Bot;
using Microsoft.Extensions.Logging;

namespace Carousel.Api.Endpoints;

/// <summary>
/// Development-only endpoints to drive the in-process Telegram bot mock.
/// Requires MockTelegramBotApi in DI (Telegram:UseMock).
/// </summary>
internal static class TelegramMockEndpoints
{
    public static IEndpointRouteBuilder MapTelegramMockEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/dev/telegram");

        api.MapPost("/message", (MockTelegramRequest request, MockTelegramBotApi mock, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TelegramMockEndpoints");
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                logger.LogWarning("Validation failed for {Path}: {Reason}", "/api/dev/telegram/message", "text is required");
                return Results.BadRequest(new { error = "text is required" });
            }

            if (request.FromUserId <= 0)
            {
                logger.LogWarning("Validation failed for {Path}: {Reason}", "/api/dev/telegram/message", "fromUserId must be positive");
                return Results.BadRequest(new { error = "fromUserId must be positive" });
            }

            mock.EnqueueUserMessage(
                request.FromUserId,
                request.Text.Trim(),
                request.FromUsername,
                request.FromFirstName);

            return Results.Accepted();
        });

        api.MapPost("/callback", (MockCallbackRequest request, MockTelegramBotApi mock, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TelegramMockEndpoints");
            if (string.IsNullOrWhiteSpace(request.Data) || request.FromUserId <= 0)
            {
                logger.LogWarning("Validation failed for {Path}: {Reason}", "/api/dev/telegram/callback", "fromUserId and data are required");
                return Results.BadRequest(new { error = "fromUserId and data are required" });
            }

            mock.EnqueueCallback(request.FromUserId, request.Data, request.FromUsername);
            return Results.Accepted();
        });

        api.MapPost("/business-connection", async (
            MockBusinessConnectionRequest request,
            TelegramBusinessConnectionService connections,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("TelegramMockEndpoints");
            if (request.TelegramUserId <= 0)
            {
                logger.LogWarning(
                    "Validation failed for {Path}: {Reason}",
                    "/api/dev/telegram/business-connection", "telegramUserId must be positive");
                return Results.BadRequest(new { error = "telegramUserId must be positive" });
            }

            var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId)
                ? $"dev-conn-{request.TelegramUserId}"
                : request.ConnectionId;
            await connections.UpsertAsync(new TelegramBusinessConnection(
                connectionId,
                request.TelegramUserId,
                string.IsNullOrWhiteSpace(request.Username) ? "devuser" : request.Username,
                request.Enabled), ct);

            return Results.Accepted();
        });

        api.MapGet("/outbox", (MockTelegramBotApi mock) =>
        {
            var messages = mock.PeekOutbox().Select(m => new
            {
                chatId = m.ChatId,
                text = m.Text,
                inlineKeyboard = m.InlineKeyboard.Select(b => new { b.Text, b.CallbackData }),
                atUtc = m.AtUtc,
            });
            return Results.Ok(messages);
        });

        api.MapDelete("/outbox", (MockTelegramBotApi mock) =>
        {
            mock.DrainOutbox();
            return Results.NoContent();
        });

        return app;
    }
}

internal sealed record MockTelegramRequest(
    long FromUserId,
    string Text,
    string? FromUsername,
    string? FromFirstName);

internal sealed record MockCallbackRequest(
    long FromUserId,
    string Data,
    string? FromUsername);

internal sealed record MockBusinessConnectionRequest(
    long TelegramUserId,
    string? Username,
    string? ConnectionId,
    bool Enabled = true);
