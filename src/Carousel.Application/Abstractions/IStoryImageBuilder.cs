using Carousel.Domain.Entities;

namespace Carousel.Application.Abstractions;

/// <summary>Байты карточки и их MIME-тип.</summary>
public sealed record StoryImageResult(byte[] Bytes, string ContentType);

/// <summary>
/// Готовит байты карточки ДО отправки в postStory: фото как есть,
/// текстовая/шаблонная карточка → серверный рендер PNG 1080×1920.
/// Единый источник правды для публикации и для режима «Превью».
/// (Печать ссылки t.me/… выполняется отдельно — при реальной отправке её делает TelegramBotApi.)
/// </summary>
public interface IStoryImageBuilder
{
    Task<StoryImageResult> BuildAsync(Publication publication, Card card, CancellationToken ct = default);
}