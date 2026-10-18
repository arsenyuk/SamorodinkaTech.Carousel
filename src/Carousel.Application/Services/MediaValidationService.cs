using Carousel.Application.Abstractions;
using Carousel.Application.Security;
using ValidationException = Carousel.Application.Security.ValidationException;

namespace Carousel.Application.Services;

public sealed class MediaValidationService : IMediaValidationService
{
    private readonly IImageProbe _probe;
    private readonly IMediaStorage _storage;
    private readonly ILimitService _limits;

    public MediaValidationService(IImageProbe probe, IMediaStorage storage, ILimitService limits)
    {
        _probe = probe;
        _storage = storage;
        _limits = limits;
    }

    public async Task<MediaValidationResult> ValidateAndSaveAsync(Guid tenantId, string contentType, byte[] content, CancellationToken ct = default)
    {
        if (content.Length == 0)
        {
            throw new ValidationException("File is empty.");
        }

        var probe = _probe.Probe(content, contentType);
        if (!probe.IsValid || probe.Width is null || probe.Height is null)
        {
            throw new ValidationException("Unsupported or corrupt image. Allowed: JPEG, PNG, WebP.");
        }

        await _limits.EnsureImageAllowedAsync(tenantId, content.LongLength, probe.Width.Value, probe.Height.Value, ct);

        var resolvedType = probe.ContentType ?? contentType;
        var saved = await _storage.SaveAsync(tenantId, resolvedType, content, probe.Width, probe.Height, ct);
        return new MediaValidationResult(saved.MediaAssetId, resolvedType, saved.SizeBytes, probe.Width.Value, probe.Height.Value);
    }

    public async Task EnsureAssetBelongsToTenantAsync(Guid tenantId, Guid mediaAssetId, CancellationToken ct = default)
    {
        var owned = await _storage.BelongsToTenantAsync(mediaAssetId, tenantId, ct);
        if (!owned)
        {
            throw new ForbiddenException("Media asset does not belong to this tenant.");
        }
    }
}
