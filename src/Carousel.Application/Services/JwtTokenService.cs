using System.Security.Claims;
using System.Text;
using Carousel.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Carousel.Application.Services;

public sealed class JwtTokenService : IJwtTokenService
{
    public const string UserIdClaim = "sub";
    public const string TelegramIdClaim = "tg";

    private readonly SymmetricSecurityKey _key;
    private readonly TokenValidationParameters _parameters;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(string signingKey, ILogger<JwtTokenService> logger)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Auth:Jwt:SigningKey must be at least 32 characters.");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    public string CreateToken(Guid userId, long telegramUserId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(UserIdClaim, userId.ToString()),
                new Claim(TelegramIdClaim, telegramUserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            }),
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddHours(24),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };

        return _handler.CreateToken(descriptor);
    }

    public async Task<Guid?> ValidateAndGetUserIdAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var result = await _handler.ValidateTokenAsync(token, _parameters);
            if (result is not { IsValid: true, ClaimsIdentity: not null })
            {
                return null;
            }

            var sub = result.ClaimsIdentity.FindFirst(UserIdClaim)?.Value;
            return sub is not null && Guid.TryParse(sub, out var userId) ? userId : null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "JWT validation failed: {Reason}", ex.Message);
            return null;
        }
    }
}
