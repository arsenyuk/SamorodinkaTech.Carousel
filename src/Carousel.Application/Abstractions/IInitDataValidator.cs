namespace Carousel.Application.Abstractions;

public interface IInitDataValidator
{
    /// <summary>
    /// Validates Telegram Mini App initData and returns parsed user profile.
    /// Throws Application.Security.ValidationException on invalid/forged data.
    /// </summary>
    InitDataUser Validate(string initData);
}

public sealed record InitDataUser(
    long TelegramUserId,
    string? Username,
    string? DisplayName,
    long? ChatId = null,
    string? ChatType = null);
