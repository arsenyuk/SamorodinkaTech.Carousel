using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Audit;

public sealed class AuditService : IAuditService
{
    private readonly CarouselDbContext _db;
    private readonly ILogger _auditLogger;
    private readonly IHttpContextAccessor _http;

    public AuditService(CarouselDbContext db, ILoggerFactory loggerFactory, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        // SourceContext "Audit" is routed by Serilog to logs/audit-*.log.
        _auditLogger = loggerFactory.CreateLogger("Audit");
        _http = httpContextAccessor;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        // UserIp: явно переданный приоритет, иначе — IP текущего запроса
        // (ForwardedHeaders уже разобрал XFF/Forwarded/X-Real-IP).
        var ip = entry.UserIp ?? ResolveRequestIp();

        _db.AuditEvents.Add(new Domain.Entities.AuditEvent
        {
            TenantId = entry.TenantId,
            UserId = entry.UserId,
            Action = entry.ActionCode,
            Login = entry.Login,
            UserIp = ip,
            Result = entry.Result,
            DetailsJson = entry.Details is null ? "{}" : JsonSerializer.Serialize(entry.Details),
        });
        await _db.SaveChangesAsync(ct);

        _auditLogger.LogInformation(
            "{ActionCode} tenant={TenantId} user={UserId} login={Login} ip={UserIp} result={Result}",
            entry.ActionCode,
            entry.TenantId,
            entry.UserId,
            entry.Login ?? "anonymous",
            ip,
            entry.Result);
    }

    private string ResolveRequestIp()
    {
        var ip = _http.HttpContext?.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
    }
}
