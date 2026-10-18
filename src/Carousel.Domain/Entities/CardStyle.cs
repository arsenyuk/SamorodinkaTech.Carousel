namespace Carousel.Domain.Entities;

public sealed class CardStyle
{
    public string FontName { get; set; } = CardStyleRules.DefaultFontName;
    public string Color { get; set; } = CardStyleRules.DefaultColor;
    public string FontSize { get; set; } = CardStyleRules.DefaultFontSize;
    public string FontStyle { get; set; } = CardStyleRules.DefaultFontStyle;
}
