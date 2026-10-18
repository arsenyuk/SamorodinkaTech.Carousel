using SkiaSharp;

namespace Carousel.Infrastructure.Templates;

public sealed record TemplateBackgroundSpec(
    string Code,
    string Name,
    string Description,
    SKColor Top,
    SKColor Bottom,
    string[] DefaultStyleFonts,
    string[] DefaultStyleColors,
    string[] DefaultStyleSizes,
    string[] DefaultStyleItalics);

public static class BackgroundGenerator
{
    public const int Size = 1080;

    public static readonly TemplateBackgroundSpec[] Specs =
    [
        new("aurora", "Аврора", "Бирюзово-фиолетовый градиент",
            SKColor.Parse("#0D7377"), SKColor.Parse("#6B4E9B"),
            ["Playfair Display", "Inter", "Inter"],
            ["#FFFFFF", "#F0F4FF", "#D9E8FF"],
            ["L", "M", "S"],
            ["false", "false", "false"]),
        new("noir", "Нуар", "Тёмный минимализм с шумом",
            SKColor.Parse("#1A1A1A"), SKColor.Parse("#000000"),
            ["Playfair Display", "Inter", "Inter"],
            ["#FFFFFF", "#E5E5E5", "#A3A3A3"],
            ["L", "M", "S"],
            ["true", "false", "false"]),
        new("sand", "Песок", "Тёплый бежево-терракотовый",
            SKColor.Parse("#F5E6D3"), SKColor.Parse("#C47B5A"),
            ["Playfair Display", "Inter", "Inter"],
            ["#3D2914", "#5C3D2E", "#8B5A3C"],
            ["L", "M", "S"],
            ["false", "false", "true"]),
        new("ocean", "Океан", "Глубокий синий переход",
            SKColor.Parse("#0A2463"), SKColor.Parse("#1E88E5"),
            ["Inter", "Playfair Display", "Inter"],
            ["#FFFFFF", "#E3F2FD", "#B3E5FC"],
            ["L", "M", "S"],
            ["false", "false", "false"]),
        new("bloom", "Цветение", "Розово-пурпурный soft",
            SKColor.Parse("#F8B4C8"), SKColor.Parse("#C2185B"),
            ["Playfair Display", "Inter", "Inter"],
            ["#FFFFFF", "#FCE4EC", "#F8BBD0"],
            ["L", "M", "S"],
            ["true", "false", "false"]),
        new("forest", "Лес", "Зелёный оливковый",
            SKColor.Parse("#2E4A2E"), SKColor.Parse("#8FBC8F"),
            ["Inter", "Playfair Display", "Inter"],
            ["#FFFFFF", "#E8F5E9", "#C8E6C9"],
            ["L", "M", "S"],
            ["false", "false", "false"]),
        new("mono", "Mono", "Светлый минимализм",
            SKColor.Parse("#F5F5F5"), SKColor.Parse("#D0D0D0"),
            ["Inter", "Playfair Display", "Inter"],
            ["#111111", "#333333", "#666666"],
            ["L", "M", "S"],
            ["false", "false", "false"]),
        new("sunset", "Закат", "Оранжево-фиолетовый закат",
            SKColor.Parse("#FF6B35"), SKColor.Parse("#7B2D8E"),
            ["Playfair Display", "Inter", "Inter"],
            ["#FFFFFF", "#FFF3E0", "#FFE0B2"],
            ["L", "M", "S"],
            ["true", "false", "false"]),
    ];

    public static byte[] GeneratePng(TemplateBackgroundSpec spec)
    {
        using var image = CreateGradientImage(spec.Top, spec.Bottom, Size);

        // Soft radial highlight for depth.
        var cx = Size * 0.3f;
        var cy = Size * 0.25f;
        var radius = Size * 0.55f;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist >= radius)
                {
                    continue;
                }

                var alpha = (1f - dist / radius) * 0.18f;
                var px = image.GetPixel(x, y);
                image.SetPixel(x, y, new SKColor(
                    (byte)(px.Red + (255 - px.Red) * alpha),
                    (byte)(px.Green + (255 - px.Green) * alpha),
                    (byte)(px.Blue + (255 - px.Blue) * alpha),
                    255));
            }
        }

        // Subtle diagonal soft bands.
        for (var i = 0; i < 3; i++)
        {
            var offset = Size * (0.2f + i * 0.25f);
            for (var y = 0; y < Size; y++)
            {
                var x0 = (int)(offset + y * 0.35f);
                for (var w = 0; w < 40; w++)
                {
                    var x = x0 + w;
                    if (x < 0 || x >= Size)
                    {
                        continue;
                    }

                    var alpha = (1f - w / 40f) * 0.06f;
                    var px = image.GetPixel(x, y);
                    image.SetPixel(x, y, new SKColor(
                        (byte)(px.Red + (255 - px.Red) * alpha),
                        (byte)(px.Green + (255 - px.Green) * alpha),
                        (byte)(px.Blue + (255 - px.Blue) * alpha),
                        255));
                }
            }
        }

        return EncodePng(image);
    }

    public static byte[] GenerateVerticalGradientPng(SKColor top, SKColor bottom, int size = Size)
    {
        using var image = CreateGradientImage(top, bottom, size);
        return EncodePng(image);
    }

    private static byte[] EncodePng(SKBitmap image)
    {
        using var skImage = SKImage.FromBitmap(image);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap CreateGradientImage(SKColor topColor, SKColor bottomColor, int size)
    {
        var image = new SKBitmap(size, size);

        // Vertical linear gradient via per-pixel fill.
        for (var y = 0; y < size; y++)
        {
            var t = (float)y / (size - 1);
            var r = (byte)(topColor.Red + (bottomColor.Red - topColor.Red) * t);
            var g = (byte)(topColor.Green + (bottomColor.Green - topColor.Green) * t);
            var b = (byte)(topColor.Blue + (bottomColor.Blue - topColor.Blue) * t);
            for (var x = 0; x < size; x++)
            {
                image.SetPixel(x, y, new SKColor(r, g, b, 255));
            }
        }

        return image;
    }
}