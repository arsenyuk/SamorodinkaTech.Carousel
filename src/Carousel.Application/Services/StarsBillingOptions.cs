namespace Carousel.Application.Services;

/// <summary>
/// Системные параметры оплаты публикации Stars: цена (целое ≥1)
/// и список username, публикующих без оплаты (@ опционален, регистр не важен).
/// </summary>
public sealed record StarsBillingOptions(int StarsPrice, string[] FreeUsernames)
{
    public bool IsExempt(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var normalized = username.Trim().TrimStart('@');
        foreach (var candidate in FreeUsernames)
        {
            if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Разбирает и проверяет конфигурацию. Бросает InvalidOperationException при невалидности.</summary>
    public static StarsBillingOptions Validate(string? starsPriceRaw, IEnumerable<string?>? freeUserNamesRaw)
    {
        if (!int.TryParse(starsPriceRaw, out var price) || price < 1)
        {
            throw new InvalidOperationException(
                $"Billing:StarsPrice must be an integer >= 1, got '{starsPriceRaw ?? "null"}'.");
        }

        var names = new List<string>();
        foreach (var raw in freeUserNamesRaw ?? Enumerable.Empty<string?>())
        {
            var name = raw?.Trim().TrimStart('@');
            if (string.IsNullOrEmpty(name) || name.Any(char.IsWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"Billing:FreeUsernames contains invalid username '{raw ?? "null"}'.");
            }

            names.Add(name);
        }

        return new StarsBillingOptions(price, names.ToArray());
    }
}