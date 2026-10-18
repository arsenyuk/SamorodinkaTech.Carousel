using Carousel.Domain.Enums;

namespace Carousel.Application.Abstractions;

public interface ITelegramAuthService
{
    Task<TelegramAuthResult> AuthenticateAsync(string initData, CancellationToken ct = default);
}

public sealed record TelegramAuthResult(long TelegramUserId, string? Username, string? DisplayName);
