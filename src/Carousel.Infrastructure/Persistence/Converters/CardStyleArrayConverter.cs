using System.Text.Json;
using Carousel.Domain.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Carousel.Infrastructure.Persistence.Converters;

public sealed class CardStyleArrayConverter : ValueConverter<CardStyle[]?, string?>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public CardStyleArrayConverter()
        : base(
            styles => styles == null
                ? null
                : JsonSerializer.Serialize(styles, SerializerOptions),
            json => string.IsNullOrEmpty(json)
                ? null
                : JsonSerializer.Deserialize<CardStyle[]>(json, SerializerOptions))
    {
    }
}

public sealed class CardStyleArrayComparer : ValueComparer<CardStyle[]?>
{
    public CardStyleArrayComparer()
        : base(
            (a, b) => Serialize(a) == Serialize(b),
            v => SerializeHashCode(v),
            v => Clone(v))
    {
    }

    private static int SerializeHashCode(CardStyle[]? styles)
    {
        var json = Serialize(styles);
        return json is null ? 0 : json.GetHashCode();
    }

    private static string? Serialize(CardStyle[]? styles)
    {
        return styles == null
            ? null
            : JsonSerializer.Serialize(styles, SerializerOptions);
    }

    private static CardStyle[]? Clone(CardStyle[]? styles)
    {
        if (styles is null)
        {
            return null;
        }

        var copy = new CardStyle[styles.Length];
        for (var i = 0; i < styles.Length; i++)
        {
            var s = styles[i];
            copy[i] = new CardStyle
            {
                FontName = s.FontName,
                Color = s.Color,
                FontSize = s.FontSize,
                FontStyle = s.FontStyle,
            };
        }

        return copy;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
