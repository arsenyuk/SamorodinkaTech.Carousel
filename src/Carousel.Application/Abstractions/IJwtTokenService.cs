namespace Carousel.Application.Abstractions;

public interface IJwtTokenService
{
    string CreateToken(Guid userId, long telegramUserId);
    Task<Guid?> ValidateAndGetUserIdAsync(string token, CancellationToken ct = default);
}

public sealed record JwtTokenResult(string AccessToken, DateTime ExpiresAtUtc);
