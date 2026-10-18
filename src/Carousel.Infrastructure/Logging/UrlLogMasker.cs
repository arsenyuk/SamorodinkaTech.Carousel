using System.Text.RegularExpressions;

namespace Carousel.Infrastructure.Logging;

internal static partial class UrlLogMasker
{
    [GeneratedRegex(@"bot\d+:[A-Za-z0-9_-]+")]
    private static partial Regex BotTokenRegex();

    /// <summary>Replaces the Telegram bot token segment with "bot***" so it never appears in logs.</summary>
    public static string MaskBotToken(string url) => BotTokenRegex().Replace(url, "bot***");
}
