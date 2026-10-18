using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Domain.Entities;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Publishing;

/// <summary>
/// Собирает байты карточки для postStory (см. <see cref="IStoryImageBuilder"/>):
/// фото из media-хранилища либо рендер текста/шаблона в PNG 1080×1920.
/// </summary>
public sealed class StoryImageBuilder : IStoryImageBuilder
{
    private readonly CarouselDbContext _db;
    private readonly IMediaStorage _mediaStorage;
    private readonly IStoryCardRenderer _renderer;
    private readonly ILogger<StoryImageBuilder> _logger;

    public StoryImageBuilder(
        CarouselDbContext db,
        IMediaStorage mediaStorage,
        IStoryCardRenderer renderer,
        ILogger<StoryImageBuilder> logger)
    {
        _db = db;
        _mediaStorage = mediaStorage;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<StoryImageResult> BuildAsync(Publication publication, Card card, CancellationToken ct = default)
    {
        if (card.MediaAssetId is not null)
        {
            var content = await _mediaStorage.GetAsync(card.MediaAssetId.Value, ct);
            if (content is null)
            {
                throw new ForbiddenException($"Media {card.MediaAssetId} missing.");
            }

            return new StoryImageResult(content.Content, content.ContentType);
        }

        if (string.IsNullOrWhiteSpace(card.Text))
        {
            throw new ValidationException("Card has neither image nor text — nothing to publish.");
        }

        // Текстовая/шаблонная карточка: фон шаблона + стили карточки (fallback — стили шаблона).
        byte[]? background = null;
        IReadOnlyList<CardStyleDto>? defaultStyles = null;
        if (publication.TemplateId is Guid templateId)
        {
            var template = await _db.CarouselTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId, ct);
            if (template is not null)
            {
                defaultStyles = ParseStyles(template.DefaultStylesJson, templateId);
                if (template.BackgroundMediaAssetId is Guid bgId)
                {
                    background = (await _mediaStorage.GetAsync(bgId, ct))?.Content;
                }
            }
        }

        var styles = card.Styles is { Length: > 0 } ? MapStyles(card.Styles) : defaultStyles;
        var png = _renderer.Render(card.Text, styles, background);
        return new StoryImageResult(png, "image/png");
    }

    private static IReadOnlyList<CardStyleDto>? MapStyles(CardStyle[]? styles) =>
        styles?.Select(s => new CardStyleDto(s.FontName, s.Color, s.FontSize, s.FontStyle)).ToList();

    private IReadOnlyList<CardStyleDto>? ParseStyles(string? json, Guid? templateId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<CardStyle>>(json)?
                .Select(s => new CardStyleDto(s.FontName, s.Color, s.FontSize, s.FontStyle))
                .ToList();
        }
        catch (JsonException ex)
        {
            // Невалидный JSON стилей шаблона — публикацию не роняем, рендер уйдёт с дефолтными стилями.
            _logger.LogWarning(ex, "Невалидный DefaultStylesJson шаблона {TemplateId} — используем стили по умолчанию",
                templateId);
            return null;
        }
    }
}