using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Bot;

/// <summary>
/// Stage 3 long-polling bot host. Conversation creates a draft publication and links Mini App.
/// No-ops when Telegram bot token is not configured.
/// </summary>
public sealed class TelegramLongPollingService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotApi _bot;
    private readonly ILogger<TelegramLongPollingService> _logger;
    private readonly string _miniAppUrl;

    public TelegramLongPollingService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotApi bot,
        ILogger<TelegramLongPollingService> logger,
        string miniAppUrl)
    {
        _scopeFactory = scopeFactory;
        _bot = bot;
        _logger = logger;
        _miniAppUrl = miniAppUrl.TrimEnd('/');
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_bot.IsConfigured)
        {
            _logger.LogInformation("Telegram bot token is not configured; long polling disabled.");
            return;
        }

        _logger.LogInformation("Telegram long polling started (mock={IsMock}).", _bot is MockTelegramBotApi);
        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdatesAsync(offset, stoppingToken);
                if (updates.Count == 0)
                {
                    // Real client uses timeout=0 short poll; avoid busy-loop.
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                    continue;
                }

                foreach (var update in updates)
                {
                    offset = Math.Max(offset, update.UpdateId + 1);
                    await HandleUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Long polling iteration failed");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleUpdateAsync(TelegramUpdate update, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarouselDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IBotConversationEngine>();

        if (update.BusinessConnection is { } connection)
        {
            // User connected/disconnected the bot in Telegram Settings → Business.
            var connections = scope.ServiceProvider.GetRequiredService<TelegramBusinessConnectionService>();
            await connections.UpsertAsync(connection, ct);
            _logger.LogInformation(
                "Business connection {ConnectionId} (user {UserId}) enabled={Enabled}.",
                connection.Id, connection.UserId, connection.IsEnabled);
            return;
        }

        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(db, engine, callback, ct);
            return;
        }

        if (update.Message is { } message)
        {
            // Успешная оплата Stars: подтверждаем Payment (payload "pub:{id}").
            if (message.SuccessfulPayment is { } payment)
            {
                var payments = scope.ServiceProvider.GetRequiredService<IPublicationStarsPaymentService>();
                var ok = await payments.CompleteAsync(
                    payment.Payload, payment.ChargeId, payment.Amount, ct,
                    userId: message.FromUserId);
                _logger.LogInformation(
                    "Stars payment {Status}: {Payload}, {Amount} {Currency}, charge {ChargeId}.",
                    ok ? "completed" : "ignored",
                    payment.Payload, payment.Amount, payment.Currency, payment.ChargeId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                await HandleMessageAsync(db, engine, message, ct);
            }
        }
    }

    private async Task HandleMessageAsync(CarouselDbContext db, IBotConversationEngine engine, TelegramMessage message, CancellationToken ct)
    {
        var text = message.Text.Trim();
        var state = await engine.GetStateAsync(message.FromUserId, ct);

        if (text.StartsWith('/'))
        {
            await HandleCommandAsync(db, engine, message, text, ct);
            return;
        }

        switch (state.Step)
        {
            case ConversationStep.AwaitingTitle:
                await OnTitleAsync(db, engine, message, text, ct);
                break;
            case ConversationStep.AwaitingDescription:
                await OnDescriptionAsync(db, engine, message, text, ct);
                break;
            case ConversationStep.AwaitingImages:
                if (text.Equals("дальше", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("skip", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("пропустить", StringComparison.OrdinalIgnoreCase))
                {
                    await GoToConfirmAsync(db, engine, message, ct);
                }
                else
                {
                    await _bot.SendMessageAsync(message.ChatId,
                        "Пришлите изображения (JPEG/PNG/WebP) через Mini App или напишите «пропустить».",
                        ct: ct);
                }

                break;
            case ConversationStep.AwaitingPublishConfirm:
                if (text.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("опубликовать", StringComparison.OrdinalIgnoreCase))
                {
                    await _bot.SendMessageAsync(message.ChatId,
                        "Откройте Mini App и нажмите «Опубликовать в мои истории».",
                        ct: ct);
                }
                else if (text.Equals("нет", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("отмена", StringComparison.OrdinalIgnoreCase))
                {
                    await engine.ClearAsync(message.FromUserId, ct);
                    await _bot.SendMessageAsync(message.ChatId, "Диалог отменён.", ct: ct);
                }
                else
                {
                    await _bot.SendMessageAsync(message.ChatId, "Ответьте «да» или «нет».", ct: ct);
                }

                break;
            default:
                await _bot.SendMessageAsync(message.ChatId,
                    "Отправьте /start чтобы создать карусель, /help — справка.",
                    ct: ct);
                break;
        }
    }

    private async Task HandleCommandAsync(CarouselDbContext db, IBotConversationEngine engine, TelegramMessage message, string command, CancellationToken ct)
    {
        var cmd = command.Split(' ')[0].ToLowerInvariant();
        switch (cmd)
        {
            case "/start":
            case "/new":
            case "/create":
            {
                var (user, tenantId) = await EnsureUserAndTenantAsync(db, message, ct);
                var publication = new Domain.Entities.Publication
                {
                    TenantId = tenantId,
                    Title = "Без названия",
                    Description = null,
                };
                db.Publications.Add(publication);
                await db.SaveChangesAsync(ct);

                await engine.AdvanceAsync(
                    message.FromUserId,
                    ConversationStep.AwaitingTitle,
                    tenantId,
                    publication.Id,
                    "{}",
                    ct);

                await _bot.SendMessageAsync(message.ChatId,
                    "Создаём карусель. Пришлите заголовок публикации (или /cancel).",
                    ct: ct);
                break;
            }
            case "/cancel":
                await engine.ClearAsync(message.FromUserId, ct);
                await _bot.SendMessageAsync(message.ChatId, "Диалог отменён.", ct: ct);
                break;
            case "/list":
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == message.FromUserId, ct);
                if (user is null)
                {
                    await _bot.SendMessageAsync(message.ChatId, "Сначала /start.", ct: ct);
                    return;
                }

                var tenantIds = await db.TenantMembers.AsNoTracking()
                    .Where(m => m.UserId == user.Id)
                    .Select(m => m.TenantId)
                    .ToListAsync(ct);
                var pubs = await db.Publications.AsNoTracking()
                    .Where(p => tenantIds.Contains(p.TenantId))
                    .OrderByDescending(p => p.CreatedAtUtc)
                    .Take(10)
                    .ToListAsync(ct);

                if (pubs.Count == 0)
                {
                    await _bot.SendMessageAsync(message.ChatId, "Публикаций пока нет. /start — создать.", ct: ct);
                    return;
                }

                var lines = pubs.Select(p => $"• {p.Title} ({p.Status}) — /preview {p.Id:N}");
                await _bot.SendMessageAsync(message.ChatId, string.Join('\n', lines), ct: ct);
                break;
            }
            case "/preview":
            {
                var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !Guid.TryParse(parts[1], out var pubId))
                {
                    await _bot.SendMessageAsync(message.ChatId, "Использование: /preview <publicationId>", ct: ct);
                    return;
                }

                var miniAppLink = $"{_miniAppUrl}/preview/{pubId}?tgInitData={message.FromUserId}";
                await _bot.SendMessageAsync(message.ChatId,
                    $"Предпросмотр: {miniAppLink}",
                    ct: ct);
                break;
            }
            case "/help":
                await _bot.SendMessageAsync(message.ChatId,
                    "Команды:\n/start — создать карусель\n/list — мои публикации\n/preview <id> — ссылка на предпросмотр\n/cancel — отменить диалог",
                    ct: ct);
                break;
            default:
                await _bot.SendMessageAsync(message.ChatId, "Неизвестная команда. /help", ct: ct);
                break;
        }
    }

    private async Task OnTitleAsync(CarouselDbContext db, IBotConversationEngine engine, TelegramMessage message, string title, CancellationToken ct)
    {
        var state = await engine.GetStateAsync(message.FromUserId, ct);
        if (state.DraftPublicationId is null)
        {
            await engine.ClearAsync(message.FromUserId, ct);
            await _bot.SendMessageAsync(message.ChatId, "Состояние потеряно. /start заново.", ct: ct);
            return;
        }

        var publication = await db.Publications.FirstOrDefaultAsync(p => p.Id == state.DraftPublicationId, ct);
        if (publication is null)
        {
            await engine.ClearAsync(message.FromUserId, ct);
            await _bot.SendMessageAsync(message.ChatId, "Черновик не найден. /start заново.", ct: ct);
            return;
        }

        publication.Title = title.Length > 300 ? title[..300] : title;
        publication.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await engine.AdvanceAsync(
            message.FromUserId,
            ConversationStep.AwaitingDescription,
            state.TenantId,
            state.DraftPublicationId,
            state.PayloadJson,
            ct);

        await _bot.SendMessageAsync(message.ChatId,
            "Теперь описание (или «пропустить»).",
            ct: ct);
    }

    private async Task OnDescriptionAsync(CarouselDbContext db, IBotConversationEngine engine, TelegramMessage message, string text, CancellationToken ct)
    {
        var state = await engine.GetStateAsync(message.FromUserId, ct);
        if (state.DraftPublicationId is null)
        {
            await engine.ClearAsync(message.FromUserId, ct);
            await _bot.SendMessageAsync(message.ChatId, "Состояние потеряно. /start заново.", ct: ct);
            return;
        }

        var publication = await db.Publications.FirstOrDefaultAsync(p => p.Id == state.DraftPublicationId, ct);
        if (publication is null)
        {
            await engine.ClearAsync(message.FromUserId, ct);
            await _bot.SendMessageAsync(message.ChatId, "Черновик не найден. /start заново.", ct: ct);
            return;
        }

        if (!text.Equals("пропустить", StringComparison.OrdinalIgnoreCase) &&
            !text.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            publication.Description = text.Length > 4000 ? text[..4000] : text;
            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        await engine.AdvanceAsync(
            message.FromUserId,
            ConversationStep.AwaitingImages,
            state.TenantId,
            state.DraftPublicationId,
            state.PayloadJson,
            ct);

        await _bot.SendMessageAsync(message.ChatId,
            "Пришлите изображения через Mini App (или напишите «пропустить», чтобы добавить карточки позже).",
            ct: ct);
    }

    private async Task GoToConfirmAsync(CarouselDbContext db, IBotConversationEngine engine, TelegramMessage message, CancellationToken ct)
    {
        var state = await engine.GetStateAsync(message.FromUserId, ct);
        await engine.AdvanceAsync(
            message.FromUserId,
            ConversationStep.AwaitingPublishConfirm,
            state.TenantId,
            state.DraftPublicationId,
            state.PayloadJson,
            ct);

        var preview = state.DraftPublicationId is null
            ? ""
            : $"\nПредпросмотр: {_miniAppUrl}/preview/{state.DraftPublicationId}?tgInitData={message.FromUserId}";
        await _bot.SendMessageAsync(message.ChatId,
            $"Черновик готов.{preview}\nОпубликовать сейчас? (да/нет). Или откройте Mini App для карточек.",
            ct: ct);
    }

    private async Task HandleCallbackAsync(CarouselDbContext db, IBotConversationEngine engine, TelegramCallbackQuery callback, CancellationToken ct)
    {
        await _bot.AnswerCallbackQueryAsync(callback.Id, null, ct);

        if (callback.Data.StartsWith("open_preview:", StringComparison.Ordinal) &&
            Guid.TryParse(callback.Data["open_preview:".Length..], out var pubId))
        {
            await _bot.SendMessageAsync(callback.FromUserId,
                $"Предпросмотр: {_miniAppUrl}/preview/{pubId}?tgInitData={callback.FromUserId}",
                ct: ct);
        }
    }

    private static async Task<(Domain.Entities.User User, Guid TenantId)> EnsureUserAndTenantAsync(
        CarouselDbContext db,
        TelegramMessage message,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == message.FromUserId, ct);
        if (user is null)
        {
            user = new Domain.Entities.User
            {
                TelegramUserId = message.FromUserId,
                TelegramUsername = message.FromUsername,
                DisplayName = message.FromFirstName,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        var membership = await db.TenantMembers.AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (membership is not null)
        {
            return (user, membership.TenantId);
        }

        var tenant = new Domain.Entities.Tenant
        {
            Name = message.FromUsername is null
                ? $"Workspace {message.FromUserId}"
                : $"@{message.FromUsername}",
            OwnerUserId = user.Id,
        };
        db.Tenants.Add(tenant);
        db.TenantMembers.Add(new Domain.Entities.TenantMember
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = MemberRole.Owner,
        });

        var free = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == "free" && p.IsActive, ct);
        if (free is not null)
        {
            db.Subscriptions.Add(new Domain.Entities.Subscription
            {
                TenantId = tenant.Id,
                PlanId = free.Id,
                Status = SubscriptionStatus.Active,
                PeriodStartUtc = DateTime.UtcNow,
                PeriodEndUtc = DateTime.UtcNow.AddDays(30),
            });
        }

        await db.SaveChangesAsync(ct);
        return (user, tenant.Id);
    }
}
