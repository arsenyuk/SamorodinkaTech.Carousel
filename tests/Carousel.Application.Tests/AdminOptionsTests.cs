using Carousel.Application.Services;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class AdminOptionsTests
{
    [Fact]
    public void Empty_config_is_not_admin()
    {
        var options = AdminOptions.Validate(null);

        options.Usernames.Should().BeEmpty();
        options.IsAdmin("devuser").Should().BeFalse();
        options.IsAdmin(null).Should().BeFalse();
        options.IsAdmin("").Should().BeFalse();
    }

    [Fact]
    public void Usernames_match_with_optional_at_and_any_case()
    {
        var options = AdminOptions.Validate(new[] { "@Vasya", "devuser" });

        options.Usernames.Should().Equal("Vasya", "devuser");
        options.IsAdmin("Vasya").Should().BeTrue();
        options.IsAdmin("@vasya").Should().BeTrue();
        options.IsAdmin("VASYA").Should().BeTrue();
        options.IsAdmin("devuser").Should().BeTrue();
        options.IsAdmin("petya").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("   ")]
    public void Invalid_username_fails_fast(string raw)
    {
        var act = () => AdminOptions.Validate(new[] { raw });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Admin:Usernames*");
    }
}