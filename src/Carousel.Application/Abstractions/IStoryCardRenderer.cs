using Carousel.Application.DTOs;

namespace Carousel.Application.Abstractions;

/// <summary>
/// Серверный рендер карточки (текст по стилям) в изображение истории Telegram
/// 1080×1920 (9:16): фон шаблона cover-кропом или белый, абзацы выровнены
/// по центру в нижней части кадра (как в превью Mini App).
/// </summary>
public interface IStoryCardRenderer
{
    byte[] Render(string? text, IReadOnlyList<CardStyleDto>? styles, byte[]? backgroundPng);
}