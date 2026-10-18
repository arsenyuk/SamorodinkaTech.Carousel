using Carousel.Application.Services;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class CleanupOptionsTests
{
    [Fact]
    public void Valid_config_parses()
    {
        var options = CleanupOptions.Validate(true, "180", "30", "24");

        options.Enabled.Should().BeTrue();
        options.PublicationRetentionDays.Should().Be(180);
        options.OrphanMediaRetentionDays.Should().Be(30);
        options.IntervalHours.Should().Be(24);
    }

    [Fact]
    public void Disabled_flag_passthrough()
    {
        var options = CleanupOptions.Validate(false, "90", "7", "12");

        options.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_publication_retention_fails_fast(string? raw)
    {
        var act = () => CleanupOptions.Validate(true, raw, "30", "24");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PublicationRetentionDays*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("x")]
    public void Invalid_orphan_media_retention_fails_fast(string raw)
    {
        var act = () => CleanupOptions.Validate(true, "180", raw, "24");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrphanMediaRetentionDays*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData(null)]
    public void Invalid_interval_fails_fast(string? raw)
    {
        var act = () => CleanupOptions.Validate(true, "180", "30", raw);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IntervalHours*");
    }
}