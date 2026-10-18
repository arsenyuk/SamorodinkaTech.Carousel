namespace Carousel.Domain;

public static class CardStyleRules
{
    public const int MaxTextLength = 500;
    public const int MaxParagraphs = 3;

    public const string DefaultFontName = "Inter";
    public const string DefaultColor = "#111111";
    public const string DefaultFontSize = "M";
    public const string DefaultFontStyle = "Normal";

    public static readonly string[] AllowedFonts = ["Inter", "Playfair Display"];
    public static readonly string[] AllowedSizes = ["S", "M", "L"];
    public static readonly string[] AllowedStyles = ["Normal", "Bold", "Italic"];

    public static string[] GetParagraphs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
    }

    public static List<string> Validate(string? text, IReadOnlyList<Entities.CardStyle>? styles)
    {
        var errors = new List<string>();

        if (text is { Length: > MaxTextLength })
        {
            errors.Add($"Text must not exceed {MaxTextLength} characters.");
        }

        var paragraphs = GetParagraphs(text);
        if (paragraphs.Length > MaxParagraphs)
        {
            errors.Add($"Text must not have more than {MaxParagraphs} paragraphs.");
        }

        if (styles is null)
        {
            return errors;
        }

        if (styles.Count > MaxParagraphs)
        {
            errors.Add($"Styles must not have more than {MaxParagraphs} entries.");
        }

        if (styles.Count > 0 && styles.Count > paragraphs.Length)
        {
            errors.Add("Styles count must not exceed the number of non-empty paragraphs.");
        }

        for (var i = 0; i < styles.Count; i++)
        {
            var style = styles[i];
            if (!AllowedFonts.Contains(style.FontName, StringComparer.Ordinal))
            {
                errors.Add($"Paragraph {i + 1}: font '{style.FontName}' is not allowed.");
            }

            if (!IsHexColor(style.Color))
            {
                errors.Add($"Paragraph {i + 1}: color '{style.Color}' must be #RRGGBB.");
            }

            if (!AllowedSizes.Contains(style.FontSize, StringComparer.Ordinal))
            {
                errors.Add($"Paragraph {i + 1}: font size '{style.FontSize}' is not allowed.");
            }

            if (!AllowedStyles.Contains(style.FontStyle, StringComparer.Ordinal))
            {
                errors.Add($"Paragraph {i + 1}: font style '{style.FontStyle}' is not allowed.");
            }
        }

        return errors;
    }

    public static bool IsHexColor(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < 7; i++)
        {
            var c = value[i];
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
