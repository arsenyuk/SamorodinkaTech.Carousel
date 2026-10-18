namespace Carousel.Application.DTOs;

public sealed record AuthTelegramRequest(string InitData);

public sealed record AuthResponse(string Token, UserDto User);

public sealed record UserDto(Guid Id, long TelegramUserId, string? TelegramUsername, string? DisplayName, bool IsAdmin = false);

// —— Админская статистика (GET /api/admin/stats, глобально по всем тенантам) ——
public sealed record AdminStatsDto(
    int Days,
    DateTime From,
    AdminStatsTotals Totals,
    IReadOnlyList<AdminDayDto> DayStats,
    IReadOnlyList<AdminTemplateStat> ByTemplate,
    IReadOnlyList<AdminBucketStat> ByCardCount,
    AdminCounters Counters);
public sealed record AdminStatsTotals(int Publications, long StarsAmount, int StarsCount);
public sealed record AdminDayDto(string Date, int Publications, long StarsAmount, int StarsCount);
public sealed record AdminTemplateStat(string Name, int Count);
public sealed record AdminBucketStat(string Bucket, int Count);
public sealed record AdminCounters(int WithBotAds, int NoAds, int WithOwnImages);

public sealed record TenantDto(Guid Id, string Name, Guid OwnerUserId, DateTime CreatedAtUtc);

public sealed record CreateTenantRequest(string Name);

public sealed record PublicationDto(
    Guid Id,
    Guid TenantId,
    string Title,
    string? Description,
    string Status,
    Guid? TemplateId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<CardDto> Cards,
    IReadOnlyList<PublicationTargetDto> Targets,
    bool NoAds = false);

/// <summary>Тело POST /publications/{id}/publish/me (опционально).</summary>
public sealed record PublishMeRequest(bool NoAds = false);

public sealed record CreatePublicationRequest(string Title, string? Description);

public sealed record UpdatePublicationRequest(string? Title, string? Description, Guid? TemplateId = null, bool ClearTemplate = false);

public sealed record TemplateDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? BackgroundMediaAssetId,
    IReadOnlyList<CardStyleDto>? DefaultStyles,
    Guid? TenantId,
    bool IsEditable);

public sealed record CreateTemplateRequest(
    string Name,
    string? Code,
    string? Description,
    IReadOnlyList<CardStyleDto>? DefaultStyles,
    Guid? BackgroundMediaAssetId,
    string? GradientFromHex,
    string? GradientToHex);

public sealed record UpdateTemplateRequest(
    string? Name,
    string? Description,
    IReadOnlyList<CardStyleDto>? DefaultStyles,
    Guid? BackgroundMediaAssetId,
    string? GradientFromHex,
    string? GradientToHex);

public sealed record CardStyleDto(string FontName, string Color, string FontSize, string FontStyle);

public sealed record CardDto(Guid Id, int Order, string? Text, Guid? MediaAssetId, IReadOnlyList<CardStyleDto>? Styles);

public sealed record CreateCardRequest(string? Text, Guid? MediaAssetId, IReadOnlyList<CardStyleDto>? Styles);

public sealed record UpdateCardRequest(string? Text, Guid? MediaAssetId, int? Order, IReadOnlyList<CardStyleDto>? Styles, bool RemoveImage = false);

public sealed record ReorderCardsRequest(IReadOnlyList<Guid> CardIds);

public sealed record MediaAssetDto(Guid Id, string ContentType, long SizeBytes, int? Width, int? Height);

public sealed record PlatformAccountDto(Guid Id, string Platform, string ExternalId, string DisplayName, string Status);

public sealed record ConnectPlatformAccountRequest(
    string Platform,
    string ExternalId,
    string DisplayName,
    string? AccessToken = null);

public sealed record PublicationTargetDto(Guid Id, string Platform, Guid PlatformAccountId, string Status);

public sealed record PublishRequest(IReadOnlyList<Guid> AccountIds);

public sealed record ChatPublishRequest(long ChatId);

public sealed record PublishResponse(
    Guid PublicationId,
    string Status,
    IReadOnlyList<DeliveryOutcomeDto> Deliveries,
    string? InvoiceUrl = null);

public sealed record DeliveryOutcomeDto(Guid TargetId, string Platform, string Status, string? ExternalPostId, string? Error);

public sealed record PlanDto(Guid Id, string Code, string Name, long PriceMinor, string Currency, IReadOnlyDictionary<string, long> Limits);

public sealed record SubscriptionDto(Guid Id, Guid PlanId, string PlanCode, string Status, DateTime PeriodStartUtc, DateTime PeriodEndUtc);

public sealed record CheckoutRequestDto(Guid PlanId, string Provider);

public sealed record CheckoutResponseDto(string CheckoutId, string? RedirectUrl, long AmountMinor, string Currency);

public sealed record TenantMemberDto(Guid Id, Guid UserId, long TelegramUserId, string? DisplayName, string Role);

public sealed record AddMemberRequest(long TelegramUserId, string Role);

public sealed record UpdateMemberRequest(string Role);

public sealed record ReadyPublicationResponse(Guid Id, string Status);
