using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Templates;
using SkiaSharp;

namespace Carousel.Infrastructure.Templates;

public sealed class TemplateBackgroundRenderer : ITemplateBackgroundRenderer
{
    public byte[] GenerateVerticalGradientPng(string fromHex, string toHex, int size = BackgroundGenerator.Size)
    {
        var top = ParseHex(fromHex);
        var bottom = ParseHex(toHex);
        return BackgroundGenerator.GenerateVerticalGradientPng(top, bottom, size);
    }

    private static SKColor ParseHex(string hex)
    {
        var value = hex.Trim();
        if (!value.StartsWith('#'))
        {
            value = $"#{value}";
        }

        return SKColor.Parse(value);
    }
}