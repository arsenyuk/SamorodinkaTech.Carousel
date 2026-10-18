namespace Carousel.Application.Abstractions;

public enum StarsPaymentState
{
    /// <summary>Оплата не требуется (exempt id или в публикации нет фото).</summary>
    NotRequired,

    /// <summary>Инвойс создан, ожидаем оплату — вернуть invoiceUrl фронту.</summary>
    Pending,

    /// <summary>Звёзды получены.</summary>
    Paid,
}

public sealed record StarsPaymentGate(StarsPaymentState State, string? InvoiceUrl);

/// <summary>
/// Гейт оплаты публикации Telegram Stars: Payment Pending/Completed
/// с идемпотентным ключом "pub:{publicationId}" (валюта XTR).
/// </summary>
public interface IPublicationStarsPaymentService
{
    /// <summary>Гейт для publish/me: исключение (free username) | Pending+инвойс | оплачено.
    /// needsPayment = есть фото ИЛИ включён крыжек «без рекламы» (цена при этом одна).</summary>
    Task<StarsPaymentGate> GetGateAsync(
        Guid tenantId,
        Guid publicationId,
        string? telegramUsername,
        string title,
        bool needsPayment,
        CancellationToken ct = default);

    /// <summary>
    /// Помечает платёж Completed по payload "pub:{id}". Идемпотентна.
    /// false = платёж не найден (логируется; путь long polling не бросает исключений).
    /// </summary>
    Task<bool> CompleteAsync(string payload, string chargeId, long? amountStars, CancellationToken ct = default, long? userId = null);

    /// <summary>Есть ли Completed-платёж публикации (для publish/retry).</summary>
    Task<bool> IsPaidAsync(Guid publicationId, CancellationToken ct = default);

    /// <summary>
    /// Автовозврат при неудачной публикации: только для «фундаментальных» кодов ошибки
    /// (повтор ничего не исправит); транзиентные фейлы оставляют оплату — retry бесплатен.
    /// true = звёзды возвращены (Payment → Refunded).
    /// </summary>
    Task<bool> TryRefundOnFailureAsync(long telegramUserId, Guid publicationId, string error, CancellationToken ct = default);
}