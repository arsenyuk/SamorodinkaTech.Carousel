namespace Carousel.Application.Services;

/// <summary>
/// Список администраторов по Telegram-алиасам (@ опционален, регистр не важен).
/// Доступ к /api/admin/* и ссылке «Статистика» в Mini App.
/// </summary>
public sealed class AdminOptions
{
    public string[] Usernames { get; init; } = [];

    public bool IsAdmin(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var normalized = username.Trim().TrimStart('@');
        foreach (var candidate in Usernames)
        {
            if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Разбирает и проверяет конфигурацию Admin:Usernames. Бросает InvalidOperationException при невалидности.</summary>
    public static AdminOptions Validate(IEnumerable<string?>? usernamesRaw)
    {
        var names = new List<string>();
        foreach (var raw in usernamesRaw ?? Enumerable.Empty<string?>())
        {
            var name = raw?.Trim().TrimStart('@');
            if (string.IsNullOrEmpty(name) || name.Any(char.IsWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"Admin:Usernames contains invalid username '{raw ?? "null"}'.");
            }

            names.Add(name);
        }

        return new AdminOptions { Usernames = names.ToArray() };
    }
}
