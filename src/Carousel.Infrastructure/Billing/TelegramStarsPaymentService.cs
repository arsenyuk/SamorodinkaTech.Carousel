using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Billing;

/// <summary>
/// Публикации за звёзды: создаёт Payment (Pending, XTR, ключ pub:{id}) + инвойс,
/// подтверждает оплату по successful_payment от Telegram.
/// </summary>
public sealed class TelegramStarsPaymentService : IPublicationStarsPaymentService
{
    private const string InvoiceDescription = "Публикация карусели в историях Telegram";

    /// <summary>Коды, при которых повтор не поможет — возвращаем звёзды.</summary>
    private static readonly string[] RefundableCodes =
    [
        "NO_BUSINESS_CONNECTION",
        "NO_CONTENT",
        "BOT_NOT_CONFIGURED",
        "BAD_CHANNEL",
    ];

    private readonly CarouselDbContext _db;
    private readonly ITelegramBotApi _bot;
    private readonly StarsBillingOptions _options;
    private readonly ILogger<TelegramStarsPaymentService> _logger;

    public TelegramStarsPaymentService(
        CarouselDbContext db,
        ITelegramBotApi bot,
        StarsBillingOptions options,
        ILogger<TelegramStarsPaymentService> logger)
    {
        _db = db;
        _bot = bot;
        _options = options;
        _logger = logger;
    }

    public async Task<StarsPaymentGate> GetGateAsync(
        Guid tenantId,
        Guid publicationId,
        string? telegramUsername,
        string title,
        bool needsPayment,
        CancellationToken ct = default)
    {
        if (_options.IsExempt(telegramUsername))
        {
            return new StarsPaymentGate(StarsPaymentState.NotRequired, null);
        }

        var key = PaymentKey(publicationId);
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct);

        if (payment is { Status: PaymentStatus.Refunded })
        {
            // После возврата нужен НОВЫЙ цикл оплаты: сбрасываем в Pending,
            // старая invoice-ссылка/charge_id больше не действительны.
            payment.Status = PaymentStatus.Pending;
            payment.ExternalId = null;
            payment.AmountMinor = _options.StarsPrice;
            await _db.SaveChangesAsync(ct);
        }

        if (payment is { Status: PaymentStatus.Completed })
        {
            return new StarsPaymentGate(StarsPaymentState.Paid, null);
        }

        if (!needsPayment)
        {
            return new StarsPaymentGate(StarsPaymentState.NotRequired, null);
        }

        if (payment is null)
        {
            payment = new Payment
            {
                TenantId = tenantId,
                Provider = BillingProvider.TelegramStars,
                Currency = "XTR",
                AmountMinor = _options.StarsPrice,
                IdempotencyKey = key,
                Status = PaymentStatus.Pending,
            };
            _db.Payments.Add(payment);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Конкурентное создание: уникальный IdempotencyKey — перечитываем чужую строку.
                _db.Entry(payment).State = EntityState.Detached;
                payment = await _db.Payments.AsNoTracking()
                    .FirstAsync(p => p.IdempotencyKey == key, ct);
            }
        }

        if (!string.IsNullOrEmpty(payment.ExternalId))
        {
            return new StarsPaymentGate(StarsPaymentState.Pending, payment.ExternalId);
        }

        var link = await _bot.CreateInvoiceLinkAsync(
            title, InvoiceDescription, key, _options.StarsPrice, ct);
        if (string.IsNullOrEmpty(link))
        {
            _logger.LogWarning(
                "createInvoiceLink failed for publication {PublicationId} (price {Price} XTR).",
                publicationId, _options.StarsPrice);
            throw new Carousel.Application.Security.ConflictException(
                "Telegram API createInvoiceLink: не удалось создать инвойс (подробности в логе сервера).");
        }

        payment.ExternalId = link;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Stars invoice created for publication {PublicationId}: {Price} XTR, {Link}",
            publicationId, _options.StarsPrice, link);
        return new StarsPaymentGate(StarsPaymentState.Pending, link);
    }

    public async Task<bool> CompleteAsync(
        string payload, string chargeId, long? amountStars, CancellationToken ct = default, long? userId = null)
    {
        if (!payload.StartsWith("pub:", StringComparison.Ordinal)
            || !Guid.TryParse(payload["pub:".Length..], out var publicationId))
        {
            _logger.LogWarning("Stars payment with unexpected payload: {Payload}", payload);
            return false;
        }

        var key = PaymentKey(publicationId);
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (payment is null)
        {
            _logger.LogWarning("Stars payment for unknown key: {Key} (charge {ChargeId})", key, chargeId);
            return false;
        }

        if (payment.Status == PaymentStatus.Completed)
        {
            return true; // идемпотентно: повторный successful_payment — no-op
        }

        payment.Status = PaymentStatus.Completed;
        // charge_id храним здесь же: он нужен для refundStarPayment (ссылка больше не нужна).
        payment.ExternalId = chargeId;
        _db.BillingEvents.Add(new BillingEvent
        {
            PaymentId = payment.Id,
            Type = "TELEGRAM_STARS_PAID",
            PayloadJson = JsonSerializer.Serialize(new
            {
                chargeId,
                amount = amountStars,
                currency = payment.Currency,
                payload,
                userId,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Stars payment completed for publication {PublicationId}: {Amount} {Currency}, charge {ChargeId}.",
            publicationId, amountStars, payment.Currency, chargeId);
        return true;
    }

    public Task<bool> IsPaidAsync(Guid publicationId, CancellationToken ct = default) =>
        _db.Payments.AsNoTracking().AnyAsync(
            p => p.IdempotencyKey == PaymentKey(publicationId)
                && p.Status == PaymentStatus.Completed,
            ct);

    public async Task<bool> TryRefundOnFailureAsync(
        long telegramUserId, Guid publicationId, string error, CancellationToken ct = default)
    {
        if (!TryParseErrorCode(error, out var code) || !RefundableCodes.Contains(code))
        {
            return false; // транзиентная ошибка — оплата остаётся, retry бесплатен
        }

        var key = PaymentKey(publicationId);
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (payment is null || payment.Status != PaymentStatus.Completed)
        {
            _logger.LogWarning(
                "Refund skipped for {Key}: payment {Status}.", key,
                payment?.Status.ToString() ?? "missing");
            return false;
        }

        if (string.IsNullOrEmpty(payment.ExternalId))
        {
            _logger.LogWarning("Refund skipped for {Key}: charge id is not stored.", key);
            return false;
        }

        var refund = await _bot.RefundStarPaymentAsync(telegramUserId, payment.ExternalId, ct);
        if (!refund.Ok)
        {
            _logger.LogWarning(
                "refundStarPayment failed for {Key}: {Code} {Description}",
                key, refund.Code, refund.Description);
            return false;
        }

        payment.Status = PaymentStatus.Refunded;
        _db.BillingEvents.Add(new BillingEvent
        {
            PaymentId = payment.Id,
            Type = "TELEGRAM_STARS_REFUNDED",
            PayloadJson = JsonSerializer.Serialize(new
            {
                chargeId = payment.ExternalId,
                userId = telegramUserId,
                reason = code,
            }),
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Stars refunded for publication {PublicationId}: {Amount} {Currency} (reason {Code}).",
            publicationId, payment.AmountMinor, payment.Currency, code);
        return true;
    }

    /// <summary>«Ошибка публикации. {CODE}: …» → CODE.</summary>
    private static bool TryParseErrorCode(string error, out string code)
    {
        code = string.Empty;
        const string prefix = "Ошибка публикации. ";
        if (!error.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = error[prefix.Length..];
        var colon = rest.IndexOf(':');
        code = colon <= 0 ? rest.Trim() : rest[..colon].Trim();
        return code.Length > 0;
    }

    private static string PaymentKey(Guid publicationId) => $"pub:{publicationId}";
}