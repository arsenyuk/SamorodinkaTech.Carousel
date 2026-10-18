namespace Carousel.Application.Abstractions;

public interface IImageProbe
{
    ImageProbeResult Probe(byte[] content, string contentType);
}

public sealed record ImageProbeResult(int? Width, int? Height, bool IsValid, string? ContentType);
