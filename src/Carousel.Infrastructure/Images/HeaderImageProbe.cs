using Carousel.Application.Abstractions;

namespace Carousel.Infrastructure.Images;

/// <summary>
/// Reads image dimensions from file headers (JPEG/PNG/WebP) without external imaging libraries.
/// Validates magic bytes; does not fully decode pixels (safer for untrusted uploads).
/// </summary>
public sealed class HeaderImageProbe : IImageProbe
{
    public ImageProbeResult Probe(byte[] content, string contentType)
    {
        if (content.Length == 0)
        {
            return new ImageProbeResult(null, null, false, null);
        }

        if (TryPng(content, out var pw, out var ph))
        {
            return new ImageProbeResult(pw, ph, true, "image/png");
        }

        if (TryJpeg(content, out var jw, out var jh))
        {
            return new ImageProbeResult(jw, jh, true, "image/jpeg");
        }

        if (TryWebp(content, out var ww, out var wh))
        {
            return new ImageProbeResult(ww, wh, true, "image/webp");
        }

        return new ImageProbeResult(null, null, false, null);
    }

    private static bool TryPng(byte[] b, out int width, out int height)
    {
        width = 0;
        height = 0;
        // 89 50 4E 47 0D 0A 1A 0A | IHDR length(4) | IHDR | width(4) | height(4)
        if (b.Length < 24 ||
            b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47 ||
            b[4] != 0x0D || b[5] != 0x0A || b[6] != 0x1A || b[7] != 0x0A)
        {
            return false;
        }

        if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R')
        {
            return false;
        }

        width = ReadInt32Be(b, 16);
        height = ReadInt32Be(b, 20);
        return width > 0 && height > 0;
    }

    private static bool TryJpeg(byte[] b, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8 || b[2] != 0xFF)
        {
            return false;
        }

        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = b[i + 1];
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }

            if (i + 4 >= b.Length)
            {
                return false;
            }

            var segmentLength = (b[i + 2] << 8) | b[i + 3];
            if (segmentLength < 2)
            {
                return false;
            }

            // SOF0–SOF15 except DHT/JPG/DAC
            var isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof)
            {
                if (i + 9 >= b.Length)
                {
                    return false;
                }

                height = (b[i + 5] << 8) | b[i + 6];
                width = (b[i + 7] << 8) | b[i + 8];
                return width > 0 && height > 0;
            }

            if (marker == 0xD9)
            {
                return false;
            }

            i += 2 + segmentLength;
        }

        return false;
    }

    private static bool TryWebp(byte[] b, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (b.Length < 30 ||
            b[0] != (byte)'R' || b[1] != (byte)'I' || b[2] != (byte)'F' || b[3] != (byte)'F' ||
            b[8] != (byte)'W' || b[9] != (byte)'E' || b[10] != (byte)'B' || b[11] != (byte)'P')
        {
            return false;
        }

        var chunk = System.Text.Encoding.ASCII.GetString(b, 12, 4);
        if (chunk == "VP8 ")
        {
            // lossy: width/height at offset 26 (little-endian 14-bit)
            if (b.Length < 30)
            {
                return false;
            }

            width = (b[26] | (b[27] << 8)) & 0x3FFF;
            height = (b[28] | (b[29] << 8)) & 0x3FFF;
            return width > 0 && height > 0;
        }

        if (chunk == "VP8L")
        {
            if (b.Length < 25)
            {
                return false;
            }

            var bits = b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24);
            width = (bits & 0x3FFF) + 1;
            height = ((bits >> 14) & 0x3FFF) + 1;
            return width > 0 && height > 0;
        }

        if (chunk == "VP8X")
        {
            if (b.Length < 30)
            {
                return false;
            }

            width = 1 + (b[24] | (b[25] << 8) | (b[26] << 16));
            height = 1 + (b[27] | (b[28] << 8) | (b[29] << 16));
            return width > 0 && height > 0;
        }

        return false;
    }

    private static int ReadInt32Be(byte[] b, int offset) =>
        (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
}
