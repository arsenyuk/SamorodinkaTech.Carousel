using Carousel.Application.Services;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class StarsBillingOptionsTests
{
    [Fact]
    public void Valid_price_and_empty_names_ok()
    {
        var options = StarsBillingOptions.Validate("1", null);

        options.StarsPrice.Should().Be(1);
        options.FreeUsernames.Should().BeEmpty();
        options.IsExempt("devuser").Should().BeFalse();
        options.IsExempt(null).Should().BeFalse();
    }

    [Fact]
    public void Usernames_match_with_optional_at_and_any_case()
    {
        var options = StarsBillingOptions.Validate("5", new[] { "@Vasya", "devuser" });

        options.StarsPrice.Should().Be(5);
        options.FreeUsernames.Should().Equal("Vasya", "devuser");
        options.IsExempt("Vasya").Should().BeTrue();
        options.IsExempt("@vasya").Should().BeTrue();
        options.IsExempt("VASYA").Should().BeTrue();
        options.IsExempt("devuser").Should().BeTrue();
        options.IsExempt("petya").Should().BeFalse();
        options.IsExempt("").Should().BeFalse();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_price_fails_fast(string? raw)
    {
        var act = () => StarsBillingOptions.Validate(raw, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*StarsPrice*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("   ")]
    public void Invalid_username_fails_fast(string raw)
    {
        var act = () => StarsBillingOptions.Validate("1", new[] { raw });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FreeUsernames*");
    }
}