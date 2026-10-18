namespace Carousel.Application.Abstractions;

public sealed record MediaValidationResult(
    Guid MediaAssetId,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height);

public interface IMediaValidationService
{
    Task<MediaValidationResult> ValidateAndSaveAsync(Guid tenantId, string contentType, byte[] content, CancellationToken ct = default);
    Task EnsureAssetBelongsToTenantAsync(Guid tenantId, Guid mediaAssetId, CancellationToken ct = default);
}
