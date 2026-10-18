using Carousel.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Carousel.Application.Tests;

public class JwtTokenServiceTests
{
    private const string Key = "unit-test-signing-key-32-bytes-min!!";

    [Fact]
    public async Task Create_and_validate_roundtrip()
    {
        var svc = new JwtTokenService(Key, NullLogger<JwtTokenService>.Instance);
        var userId = Guid.NewGuid();
        var token = svc.CreateToken(userId, 999);

        var parsed = await svc.ValidateAndGetUserIdAsync(token);

        parsed.Should().Be(userId);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var svc = new JwtTokenService(Key, NullLogger<JwtTokenService>.Instance);
        var token = svc.CreateToken(Guid.NewGuid(), 1);
        var bad = token[..^2] + "xx";

        var parsed = await svc.ValidateAndGetUserIdAsync(bad);

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task Wrong_key_is_rejected()
    {
        var issuer = new JwtTokenService(Key, NullLogger<JwtTokenService>.Instance);
        var other = new JwtTokenService("another-signing-key-32-bytes-min!", NullLogger<JwtTokenService>.Instance);
        var token = issuer.CreateToken(Guid.NewGuid(), 1);

        var parsed = await other.ValidateAndGetUserIdAsync(token);

        parsed.Should().BeNull();
    }

    [Fact]
    public void Short_key_throws()
    {
        var act = () => new JwtTokenService("short", NullLogger<JwtTokenService>.Instance);
        act.Should().Throw<InvalidOperationException>();
    }
}
