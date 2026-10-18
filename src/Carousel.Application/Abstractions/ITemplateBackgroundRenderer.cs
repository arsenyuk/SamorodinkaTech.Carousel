namespace Carousel.Application.Abstractions;

public interface ITemplateBackgroundRenderer
{
    byte[] GenerateVerticalGradientPng(string fromHex, string toHex, int size = 1080);
}
