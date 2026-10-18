using Carousel.Application.DTOs;
using Carousel.Infrastructure.Publishing;
using Carousel.Infrastructure.Templates;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Carousel.Application.Tests;

public class StoryCardRendererTests
{
    private readonly StoryCardRenderer _renderer = new();

    [Fact]
    public void Render_text_only_produces_1080x1920_png()
    {
        var png = _renderer.Render("Привет, это карточка", null, null);

        png[0].Should().Be(0x89);
        png[1].Should().Be((byte)'P');
        using var bitmap = SKBitmap.Decode(png);
        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(StoryCardRenderer.Width);
        bitmap.Height.Should().Be(StoryCardRenderer.Height);
    }

    [Fact]
    public void Render_with_template_background_covers_9x16_frame()
    {
        // Фон шаблона квадратный (1080×1080) — cover-кроп в 9:16 без искажений кадра.
        var background = BackgroundGenerator.GenerateVerticalGradientPng(
            SKColor.Parse("#0D7377"), SKColor.Parse("#6B4E9B"), 1080);

        var png = _renderer.Render("Текст на фоне", null, background);

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(StoryCardRenderer.Width);
        bitmap.Height.Should().Be(StoryCardRenderer.Height);
        // Кадр не белый — фон реально отрисован.
        bitmap.GetPixel(540, 960).Red.Should().NotBe(bitmap.GetPixel(540, 960).Green,
            "пиксель в центре должен нести цвета градиента фона");
    }

    [Fact]
    public void Render_empty_text_returns_valid_white_png()
    {
        var png = _renderer.Render(null, null, null);

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(StoryCardRenderer.Width);
        bitmap.Height.Should().Be(StoryCardRenderer.Height);
        var pixel = bitmap.GetPixel(10, 10);
        pixel.Red.Should().Be(255);
        pixel.Green.Should().Be(255);
        pixel.Blue.Should().Be(255);
    }

    [Fact]
    public void Render_multiline_with_styles_and_bad_inputs_is_valid()
    {
        var styles = new List<CardStyleDto>
        {
            new("Playfair Display", "#FF0000", "L", "Bold"),
            new("Inter", "not-a-color", "S", "Italic"),
        };

        var png = _renderer.Render("Абзац первый\nАбзац второй с длинным переносом слов для проверки wrap", styles, null);

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(StoryCardRenderer.Width);

        // Битые фон/цвет не должны ронять рендер.
        var withBrokenBackground = _renderer.Render("Текст", null, "не png"u8.ToArray());
        withBrokenBackground.Should().NotBeEmpty();
    }
}