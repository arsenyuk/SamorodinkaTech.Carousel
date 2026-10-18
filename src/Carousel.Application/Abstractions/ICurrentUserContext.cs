namespace Carousel.Application.Abstractions;

public sealed record CurrentUser(Guid UserId, long TelegramUserId, string? TelegramUsername, string? DisplayName);

public interface ICurrentUserContext
{
    Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default);
    Guid? TenantId { get; }
    bool IsDevelopment { get; }
}
