using Carousel.Infrastructure.Images;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Carousel.Application.Tests;

public class ImageProbeTests
{
    private readonly HeaderImageProbe _probe = new();

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    [Fact]
    public void Probe_png_returns_dimensions()
    {
        var png = CreatePng(1, 1);

        var result = _probe.Probe(png, "application/octet-stream");

        result.IsValid.Should().BeTrue();
        result.Width.Should().Be(1);
        result.Height.Should().Be(1);
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Probe_jpeg_magic_bytes_detected()
    {
        var jpeg = CreateJpeg(2, 3);

        var result = _probe.Probe(jpeg, "text/plain");

        result.IsValid.Should().BeTrue();
        result.Width.Should().Be(2);
        result.Height.Should().Be(3);
        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public void Probe_rejects_text_as_image()
    {
        var result = _probe.Probe("hello world"u8.ToArray(), "image/png");

        result.IsValid.Should().BeFalse();
        result.ContentType.Should().BeNull();
    }

    [Fact]
    public void Probe_rejects_empty()
    {
        var result = _probe.Probe([], "image/png");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Probe_rejects_gif()
    {
        // GIF87a header
        var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0, 0 };
        var result = _probe.Probe(gif, "image/gif");
        result.IsValid.Should().BeFalse();
    }
}
