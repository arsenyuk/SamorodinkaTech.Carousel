namespace Carousel.Application.Abstractions;

public sealed record AuditEntry(
    string ActionCode,
    Guid? TenantId = null,
    Guid? UserId = null,
    string? Login = null,
    string? UserIp = null,
    string? Result = null,
    object? Details = null);

public interface IAuditService
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}
