using Carousel.Application.DTOs;
using Carousel.Domain;
using Carousel.Domain.Entities;
using FluentValidation;

namespace Carousel.Application.Validators;

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreatePublicationRequestValidator : AbstractValidator<CreatePublicationRequest>
{
    public CreatePublicationRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

public sealed class UpdatePublicationRequestValidator : AbstractValidator<UpdatePublicationRequest>
{
    public UpdatePublicationRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(300).When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(4000).When(x => x.Description is not null);
    }
}

public sealed class CreateCardRequestValidator : AbstractValidator<CreateCardRequest>
{
    public CreateCardRequestValidator()
    {
        RuleFor(x => x.Text).MaximumLength(CardStyleRules.MaxTextLength);
        RuleFor(x => x).Must(x => x.MediaAssetId is not null || !string.IsNullOrWhiteSpace(x.Text))
            .WithMessage("Card must have either text or media.");
        RuleFor(x => x).Must(x => CardStyleRules.Validate(x.Text, ToStyles(x.Styles)).Count == 0)
            .WithMessage(x => string.Join(" ", CardStyleRules.Validate(x.Text, ToStyles(x.Styles))));
    }

    internal static CardStyle[]? ToStyles(IReadOnlyList<CardStyleDto>? styles)
    {
        if (styles is null)
        {
            return null;
        }

        return styles
            .Select(s => new CardStyle
            {
                FontName = s.FontName,
                Color = s.Color,
                FontSize = s.FontSize,
                FontStyle = s.FontStyle,
            })
            .ToArray();
    }
}

public sealed class UpdateCardRequestValidator : AbstractValidator<UpdateCardRequest>
{
    public UpdateCardRequestValidator()
    {
        RuleFor(x => x.Text).MaximumLength(CardStyleRules.MaxTextLength).When(x => x.Text is not null);
        RuleFor(x => x.Order).InclusiveBetween(0, int.MaxValue).When(x => x.Order is not null);
        When(x => x.Styles is not null, () =>
        {
            RuleFor(x => x.Styles!)
                .Must(styles => styles.Count <= CardStyleRules.MaxParagraphs)
                .WithMessage($"Styles must not have more than {CardStyleRules.MaxParagraphs} entries.");
            RuleFor(x => x.Styles!)
                .Must(StylesEntriesAreValid)
                .WithMessage("One or more style entries are invalid: font, color, size or style value is not allowed.");
        });
    }

    internal static bool StylesEntriesAreValid(IReadOnlyList<CardStyleDto> styles)
    {
        foreach (var style in styles)
        {
            if (!CardStyleRules.AllowedFonts.Contains(style.FontName, StringComparer.Ordinal)
                || !CardStyleRules.IsHexColor(style.Color)
                || !CardStyleRules.AllowedSizes.Contains(style.FontSize, StringComparer.Ordinal)
                || !CardStyleRules.AllowedStyles.Contains(style.FontStyle, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class CreateTemplateRequestValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code)
            .Matches("^[a-z0-9-]{1,50}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Code))
            .WithMessage("Code must match ^[a-z0-9-]{1,50}$.");
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        When(x => x.DefaultStyles is not null, () =>
        {
            RuleFor(x => x.DefaultStyles!)
                .Must(styles => styles.Count is >= 0 and <= CardStyleRules.MaxParagraphs)
                .WithMessage($"DefaultStyles must not have more than {CardStyleRules.MaxParagraphs} entries.");
            RuleFor(x => x.DefaultStyles!)
                .Must(UpdateCardRequestValidator.StylesEntriesAreValid)
                .WithMessage("One or more style entries are invalid: font, color, size or style value is not allowed.");
        });
        RuleFor(x => x).Must(BackgroundIsValid)
            .WithMessage("Background is required: provide BackgroundMediaAssetId or both GradientFromHex and GradientToHex.");
        RuleFor(x => x.GradientFromHex)
            .Must(CardStyleRules.IsHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.GradientFromHex))
            .WithMessage("GradientFromHex must be a valid hex color.");
        RuleFor(x => x.GradientToHex)
            .Must(CardStyleRules.IsHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.GradientToHex))
            .WithMessage("GradientToHex must be a valid hex color.");
    }

    private static bool BackgroundIsValid(CreateTemplateRequest request)
    {
        var hasMedia = request.BackgroundMediaAssetId is not null;
        var hasGradient = !string.IsNullOrWhiteSpace(request.GradientFromHex)
            && !string.IsNullOrWhiteSpace(request.GradientToHex);
        return hasMedia ^ hasGradient;
    }
}

public sealed class UpdateTemplateRequestValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        When(x => x.DefaultStyles is not null, () =>
        {
            RuleFor(x => x.DefaultStyles!)
                .Must(styles => styles.Count is >= 0 and <= CardStyleRules.MaxParagraphs)
                .WithMessage($"DefaultStyles must not have more than {CardStyleRules.MaxParagraphs} entries.");
            RuleFor(x => x.DefaultStyles!)
                .Must(UpdateCardRequestValidator.StylesEntriesAreValid)
                .WithMessage("One or more style entries are invalid: font, color, size or style value is not allowed.");
        });
        // Background optional on update: null = keep existing. If any background field set — XOR required.
        When(x => x.BackgroundMediaAssetId is not null
            || !string.IsNullOrWhiteSpace(x.GradientFromHex)
            || !string.IsNullOrWhiteSpace(x.GradientToHex), () =>
        {
            RuleFor(x => x).Must(BackgroundIsValid)
                .WithMessage("Background must be either BackgroundMediaAssetId or both GradientFromHex and GradientToHex.");
        });
        RuleFor(x => x.GradientFromHex)
            .Must(CardStyleRules.IsHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.GradientFromHex))
            .WithMessage("GradientFromHex must be a valid hex color.");
        RuleFor(x => x.GradientToHex)
            .Must(CardStyleRules.IsHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.GradientToHex))
            .WithMessage("GradientToHex must be a valid hex color.");
    }

    private static bool BackgroundIsValid(UpdateTemplateRequest request)
    {
        var hasMedia = request.BackgroundMediaAssetId is not null;
        var hasGradient = !string.IsNullOrWhiteSpace(request.GradientFromHex)
            && !string.IsNullOrWhiteSpace(request.GradientToHex);
        return hasMedia ^ hasGradient;
    }
}

public sealed class AuthTelegramRequestValidator : AbstractValidator<AuthTelegramRequest>
{
    public AuthTelegramRequestValidator()
    {
        RuleFor(x => x.InitData).NotEmpty();
    }
}

public sealed class ConnectPlatformAccountRequestValidator : AbstractValidator<ConnectPlatformAccountRequest>
{
    public ConnectPlatformAccountRequestValidator()
    {
        RuleFor(x => x.Platform).NotEmpty().Must(p => p is "Telegram" or "Instagram" or "telegram" or "instagram");
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public sealed class PublishRequestValidator : AbstractValidator<PublishRequest>
{
    public PublishRequestValidator()
    {
        RuleFor(x => x.AccountIds).NotEmpty();
    }
}

public sealed class CheckoutRequestDtoValidator : AbstractValidator<CheckoutRequestDto>
{
    public CheckoutRequestDtoValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Provider).NotEmpty().Must(p => p is "TelegramStars" or "Web" or "telegramstars" or "web");
    }
}

public sealed class ReorderCardsRequestValidator : AbstractValidator<ReorderCardsRequest>
{
    public ReorderCardsRequestValidator()
    {
        RuleFor(x => x.CardIds).NotEmpty();
        RuleFor(x => x.CardIds).Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Card ids must be unique.");
    }
}
