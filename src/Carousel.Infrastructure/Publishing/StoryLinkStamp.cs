using SkiaSharp;

namespace Carousel.Infrastructure.Publishing;

/// <summary>
/// Печатает видимую ссылку (t.me/…) в правом нижнем углу фото перед отправкой в историю.
/// Метод идемпотентен по смыслу: без текста или при невозможности декодировать — возвращает исходник.
/// </summary>
public static class StoryLinkStamp
{
    public static byte[] Stamp(byte[] photo, string contentType, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return photo;
        }

        SKBitmap? bitmap;
        try
        {
            bitmap = SKBitmap.Decode(photo);
        }
        catch
        {
            return photo;
        }

        if (bitmap is null)
        {
            return photo;
        }

        using (bitmap)
        {
            using var surface = SKSurface.Create(bitmap.Info);
            if (surface is null)
            {
                return photo;
            }

            var canvas = surface.Canvas;
            canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default);

            // Подписи-пилюли в правом нижнем углу.
            var margin = MathF.Max(10f, bitmap.Width * 0.02f);
            var fontSize = MathF.Max(20f, bitmap.Width * 0.037f);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var pillPaint = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = true };

            var textWidth = font.MeasureText(text);
            var padX = fontSize * 0.55f;
            var pillW = textWidth + padX * 2;
            var pillH = fontSize * 1.7f;
            var pillX = bitmap.Width - pillW - margin;
            var pillY = bitmap.Height - pillH - margin;
            if (pillX < 0 || pillY < 0)
            {
                return photo; // изображение мельче подписи — не ломаем кадр
            }

            canvas.DrawRoundRect(
                new SKRect(pillX, pillY, pillX + pillW, pillY + pillH),
                pillH / 2f, pillH / 2f, pillPaint);
            canvas.DrawText(
                text, pillX + padX, pillY + (pillH + fontSize * 0.35f) / 2f,
                SKTextAlign.Left, font, textPaint);

            using var stamped = surface.Snapshot();
            var format = contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase)
                ? SKEncodedImageFormat.Jpeg
                : SKEncodedImageFormat.Png;
            using var encoded = stamped.Encode(format, 92);
            return encoded is null ? photo : encoded.ToArray();
        }
    }
}