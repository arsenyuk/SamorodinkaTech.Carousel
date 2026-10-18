using System.Net;
using Carousel.Application.Abstractions;
using Serilog.Context;

namespace Carousel.Api.Middleware;

/// <summary>
/// Каждому запросу API в LogContext добавляются ClientIp и UserName —
/// попадают в app-лог (outputTemplate) для request logging, ошибок и бизнес-логов
/// в скоупе запроса.
/// Клиентский IP: XFF (разобран ForwardedHeaders) → X-Real-IP → Forwarded (RFC 7239)
/// → TCP-пир. Альтернативы парсим сами: в ASP.NET Core их нет в ForwardedHeaders enum.
/// </summary>
public sealed class RequestLogContextMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLogContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = ResolveClientIp(context);

        var userName = "-";
        try
        {
            var currentUser = context.RequestServices.GetRequiredService<ICurrentUserContext>();
            var user = await currentUser.GetCurrentUserAsync(context.RequestAborted);
            if (!string.IsNullOrWhiteSpace(user?.TelegramUsername))
            {
                userName = user.TelegramUsername!;
            }
        }
        catch
        {
            // Невалидный/отсутствующий токен или dev-заголовок: запрос не ломаем —
            // реальный 401/403 выдаст сам endpoint; в лог пишем "-".
        }

        using (LogContext.PushProperty("ClientIp", clientIp))
        using (LogContext.PushProperty("UserName", userName))
        {
            await _next(context);
        }
    }

    private static string ResolveClientIp(HttpContext context)
    {
        IPAddress? ip;

        if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
        {
            // XFF уже разобран ForwardedHeadersMiddleware (справа налево, правый =
            // последний добавленный прокси = реальный клиент при append-семантике).
            ip = context.Connection.RemoteIpAddress;
        }
        else if (context.Request.Headers.TryGetValue("X-Real-IP", out var real)
            && IPAddress.TryParse(real.ToString().Trim(), out var realIp))
        {
            ip = realIp;
        }
        else if (context.Request.Headers.TryGetValue("Forwarded", out var forwarded)
            && TryParseForwarded(forwarded.ToString(), out var forwardedIp))
        {
            ip = forwardedIp;
        }
        else
        {
            ip = context.Connection.RemoteIpAddress;
        }

        if (ip is null)
        {
            return "unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        // Нормализуем RemoteIpAddress, чтобы аудит/ACCESS_DENIED видели тот же IP.
        context.Connection.RemoteIpAddress = ip;
        return ip.ToString();
    }

    /// <summary>Минимальный разбор Forwarded: первый валидный for= из списка (RFC 7239).</summary>
    private static bool TryParseForwarded(string header, out IPAddress? ip)
    {
        ip = null;
        foreach (var element in header.Split(','))
        {
            foreach (var pair in element.Split(';'))
            {
                var trimmed = pair.Trim();
                if (!trimmed.StartsWith("for=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = trimmed[4..].Trim().Trim('"');
                if (value.Length == 0
                    || value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith('_'))
                {
                    continue;
                }

                // [2001:db8::1]:4100 → убираем скобки и порт; ipv4:port → убираем порт;
                // голый ipv6 (много двоеточий) не трогаем.
                if (value.StartsWith('['))
                {
                    var end = value.IndexOf(']');
                    value = end > 0 ? value[1..end] : value;
                }
                else
                {
                    var colon = value.LastIndexOf(':');
                    if (colon > 0
                        && value.IndexOf(':') == colon
                        && int.TryParse(value[(colon + 1)..], out _))
                    {
                        value = value[..colon];
                    }
                }

                if (IPAddress.TryParse(value, out var parsed))
                {
                    ip = parsed;
                    return true;
                }
            }
        }

        return false;
    }
}