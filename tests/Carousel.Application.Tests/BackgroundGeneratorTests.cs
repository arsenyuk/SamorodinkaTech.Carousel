using Carousel.Infrastructure.Templates;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class BackgroundGeneratorTests
{
    [Fact]
    public void Specs_have_eight_unique_codes()
    {
        BackgroundGenerator.Specs.Should().HaveCount(8);
        BackgroundGenerator.Specs.Select(s => s.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GeneratePng_returns_non_empty_png_for_each_spec()
    {
        foreach (var spec in BackgroundGenerator.Specs)
        {
            var png = BackgroundGenerator.GeneratePng(spec);
            png.Should().NotBeEmpty();
            // PNG magic bytes
            png[0].Should().Be(0x89);
            png[1].Should().Be(0x50);
            png[2].Should().Be(0x4E);
            png[3].Should().Be(0x47);
            png.Length.Should().BeGreaterThan(1000);
        }
    }
}
