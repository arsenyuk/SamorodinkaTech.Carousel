using System.Security.Cryptography;
using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Storage;

public sealed class PostgresByteaMediaStorage : IMediaStorage
{
    private readonly CarouselDbContext _db;

    public PostgresByteaMediaStorage(CarouselDbContext db)
    {
        _db = db;
    }

    public async Task<MediaStorageResult> SaveAsync(
        Guid tenantId,
        string contentType,
        byte[] content,
        int? width = null,
        int? height = null,
        CancellationToken ct = default)
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var existing = await _db.MediaAssets.FirstOrDefaultAsync(
            a => a.TenantId == tenantId && a.Sha256 == sha256,
            ct);

        if (existing is not null)
        {
            return new MediaStorageResult(existing.Id, existing.StorageKey, existing.Sha256, existing.SizeBytes, existing.Width, existing.Height);
        }

        var asset = new Domain.Entities.MediaAsset
        {
            TenantId = tenantId,
            ContentType = contentType,
            SizeBytes = content.LongLength,
            Sha256 = sha256,
            StorageKey = $"pg://{tenantId:N}/{Guid.NewGuid():N}",
            Content = content,
            Width = width,
            Height = height,
        };

        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync(ct);

        return new MediaStorageResult(asset.Id, asset.StorageKey, asset.Sha256, asset.SizeBytes, asset.Width, asset.Height);
    }

    public async Task<MediaContent?> GetAsync(Guid mediaAssetId, CancellationToken ct = default)
    {
        var asset = await _db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mediaAssetId, ct);
        if (asset?.Content is null)
        {
            return null;
        }

        return new MediaContent(asset.ContentType, asset.Content);
    }

    public async Task<bool> BelongsToTenantAsync(Guid mediaAssetId, Guid tenantId, CancellationToken ct = default)
    {
        return await _db.MediaAssets.AsNoTracking()
            .AnyAsync(a => a.Id == mediaAssetId && a.TenantId == tenantId, ct);
    }
}
