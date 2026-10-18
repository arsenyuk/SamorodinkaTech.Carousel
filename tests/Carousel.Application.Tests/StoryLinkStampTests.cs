using Carousel.Infrastructure.Images;
using Carousel.Infrastructure.Publishing;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Carousel.Application.Tests;

public class StoryLinkStampTests
{
    private static byte[] MakeImage(SKEncodedImageFormat format, int width = 400, int height = 300)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    [Fact]
    public void Stamp_png_keeps_dimensions_and_changes_bytes()
    {
        var original = MakeImage(SKEncodedImageFormat.Png);
        var probe = new HeaderImageProbe();

        var stamped = StoryLinkStamp.Stamp(original, "image/png", "t.me/carousel_bot");

        stamped.Should().NotBeSameAs(original);
        stamped.Should().NotEqual(original, "подпись должна изменить пиксели");
        var result = probe.Probe(stamped, "image/png");
        result.IsValid.Should().BeTrue();
        result.ContentType.Should().Be("image/png");
        result.Width.Should().Be(400);
        result.Height.Should().Be(300);
    }

    [Fact]
    public void Stamp_jpeg_keeps_format_and_dimensions()
    {
        var original = MakeImage(SKEncodedImageFormat.Jpeg, 640, 480);
        var probe = new HeaderImageProbe();

        var stamped = StoryLinkStamp.Stamp(original, "image/jpeg", "t.me/carousel_bot");

        var result = probe.Probe(stamped, "image/jpeg");
        result.IsValid.Should().BeTrue();
        result.ContentType.Should().Be("image/jpeg");
        result.Width.Should().Be(640);
        result.Height.Should().Be(480);
    }

    [Fact]
    public void Stamp_with_empty_text_returns_original()
    {
        var original = MakeImage(SKEncodedImageFormat.Png);

        StoryLinkStamp.Stamp(original, "image/png", "").Should().BeSameAs(original);
        StoryLinkStamp.Stamp(original, "image/png", "   ").Should().BeSameAs(original);
        StoryLinkStamp.Stamp(original, "image/png", null!).Should().BeSameAs(original);
    }

    [Fact]
    public void Stamp_with_undecodable_bytes_returns_original()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5 };

        StoryLinkStamp.Stamp(garbage, "image/png", "t.me/x").Should().BeSameAs(garbage);
    }

    [Fact]
    public void Stamp_tiny_image_skips_watermark_safely()
    {
        var original = MakeImage(SKEncodedImageFormat.Png, width: 10, height: 10);

        var stamped = StoryLinkStamp.Stamp(original, "image/png", "t.me/very_long_bot_name");

        stamped.Should().BeSameAs(original, "пилюля не влезает — кадр не ломаем");
    }
}