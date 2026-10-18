namespace Carousel.Application.Abstractions;

public interface IMediaStorage
{
    Task<MediaStorageResult> SaveAsync(Guid tenantId, string contentType, byte[] content, int? width = null, int? height = null, CancellationToken ct = default);
    Task<MediaContent?> GetAsync(Guid mediaAssetId, CancellationToken ct = default);
    Task<bool> BelongsToTenantAsync(Guid mediaAssetId, Guid tenantId, CancellationToken ct = default);
}

public sealed record MediaStorageResult(Guid MediaAssetId, string StorageKey, string Sha256, long SizeBytes, int? Width, int? Height);
public sealed record MediaContent(string ContentType, byte[] Content);
