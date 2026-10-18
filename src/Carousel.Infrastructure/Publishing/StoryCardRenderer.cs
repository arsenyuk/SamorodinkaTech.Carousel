using System.Collections.Concurrent;
using System.IO;
using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Domain;
using SkiaSharp;

namespace Carousel.Infrastructure.Publishing;

/// <summary>
/// Рендерит карточку в PNG 1080×1920 для postStory: фон шаблона (cover-кроп) или белый,
/// абзацы стилей (Inter/Playfair, S/M/L, Bold/Italic) по центру нижней трети кадра —
/// как в превью Mini App. Файлы шрифтов: Assets/Fonts (копируются в output).
/// </summary>
public sealed class StoryCardRenderer : IStoryCardRenderer
{
    public const int Width = 1080;
    public const int Height = 1920;

    private const float SideMargin = 80f;
    private const float BottomMargin = 170f; // место под пилюлю ссылки бота
    private const float LineHeightFactor = 1.4f;
    private const float ParagraphGapFactor = 0.5f;

    private static readonly ConcurrentDictionary<string, SKTypeface> Typefaces = new();

    private static readonly Dictionary<string, float> SizeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S"] = 40f,
        ["M"] = 52f,
        ["L"] = 68f,
    };

    public byte[] Render(string? text, IReadOnlyList<CardStyleDto>? styles, byte[]? backgroundPng)
    {
        using var bitmap = new SKBitmap(Width, Height);
        using var surface = SKSurface.Create(bitmap.Info)
            ?? throw new InvalidOperationException("Cannot create render surface for story card.");
        var canvas = surface.Canvas;

        DrawBackground(canvas, backgroundPng);

        var paragraphs = CardStyleRules.GetParagraphs(text);
        if (paragraphs.Length > 0)
        {
            DrawText(canvas, paragraphs, styles);
        }

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
        {
            throw new InvalidOperationException("Failed to encode story card PNG.");
        }

        return encoded.ToArray();
    }

    private static void DrawBackground(SKCanvas canvas, byte[]? backgroundPng)
    {
        SKBitmap? bg = null;
        if (backgroundPng is { Length: > 0 })
        {
            try
            {
                bg = SKBitmap.Decode(backgroundPng);
            }
            catch
            {
                // Невалидные байты фона — не роняем публикацию, рисуем белый фон.
                bg = null;
            }
        }

        using (bg)
        {
            if (bg is null)
            {
                canvas.Clear(SKColors.White);
                return;
            }

            // Cover-кроп в 9:16 (как Telegram показывает фото в истории).
            var scale = Math.Max((float)Width / bg.Width, (float)Height / bg.Height);
            var dx = (Width - bg.Width * scale) / 2f;
            var dy = (Height - bg.Height * scale) / 2f;
            canvas.Save();
            canvas.Translate(dx, dy);
            canvas.Scale(scale, scale);
            canvas.DrawBitmap(bg, 0, 0, SKSamplingOptions.Default);
            canvas.Restore();
        }
    }

    private static void DrawText(SKCanvas canvas, string[] paragraphs, IReadOnlyList<CardStyleDto>? styles)
    {
        var maxWidth = Width - SideMargin * 2;

        // Собираем линии всех абзацев с их стилями.
        var lines = new List<(string Text, SKFont Font, SKColor Color, float Size, float Descent)>();
        for (var i = 0; i < paragraphs.Length; i++)
        {
            var style = styles is not null && i < styles.Count ? styles[i] : null;
            var size = SizeMap.TryGetValue(style?.FontSize ?? CardStyleRules.DefaultFontSize, out var s)
                ? s
                : SizeMap[CardStyleRules.DefaultFontSize];
            var typeface = ResolveTypeface(
                style?.FontName ?? CardStyleRules.DefaultFontName,
                style?.FontStyle ?? CardStyleRules.DefaultFontStyle);
            var font = new SKFont(typeface, size);
            var color = ParseColor(style?.Color ?? CardStyleRules.DefaultColor);

            foreach (var line in Wrap(paragraphs[i], font, maxWidth))
            {
                lines.Add((line, font, color, size, size * (LineHeightFactor - 1f)));
            }

            if (i < paragraphs.Length - 1)
            {
                lines.Add((string.Empty, font, color, size, size * (LineHeightFactor - 1f + ParagraphGapFactor)));
            }
        }

        // Блок прижат к низу кадра и отцентрован — как капшен в превью.
        var totalHeight = lines.Sum(l => l.Size + l.Descent);
        var y = Height - BottomMargin - totalHeight;

        using var paint = new SKPaint { IsAntialias = true };
        foreach (var line in lines)
        {
            y += line.Size;
            if (line.Text.Length > 0)
            {
                paint.Color = line.Color;
                canvas.DrawText(line.Text, Width / 2f, y, SKTextAlign.Center, line.Font, paint);
                y += line.Descent;
            }
            else
            {
                y += line.Descent;
            }
        }
    }

    private static IEnumerable<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            yield break;
        }

        var current = words[0];
        for (var i = 1; i < words.Length; i++)
        {
            var candidate = current + " " + words[i];
            if (font.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            yield return current;
            current = words[i];
        }

        yield return current;
    }

    private static SKTypeface ResolveTypeface(string fontName, string fontStyle)
    {
        var family = fontName.Contains("Playfair", StringComparison.OrdinalIgnoreCase)
            ? "PlayfairDisplay"
            : "Inter";
        var style = fontStyle.Equals("Italic", StringComparison.OrdinalIgnoreCase) ? "Italic"
            : fontStyle.Equals("Bold", StringComparison.OrdinalIgnoreCase) ? "Bold"
            : "Regular";
        var key = family + "-" + style;

        return Typefaces.GetOrAdd(key, k =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", k + ".ttf");
            if (File.Exists(path))
            {
                try
                {
                    return SKTypeface.FromFile(path) ?? SKTypeface.Default;
                }
                catch
                {
                    return SKTypeface.Default;
                }
            }

            return SKTypeface.Default;
        });
    }

    private static SKColor ParseColor(string hex)
    {
        try
        {
            return SKColor.Parse(hex.StartsWith('#') ? hex : "#" + hex);
        }
        catch
        {
            return SKColor.Parse(CardStyleRules.DefaultColor);
        }
    }
}