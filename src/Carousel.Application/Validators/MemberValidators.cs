using Carousel.Application.DTOs;
using Carousel.Domain.Enums;
using FluentValidation;

namespace Carousel.Application.Validators;

public sealed class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator()
    {
        RuleFor(x => x.TelegramUserId).GreaterThan(0);
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.Role).Must(BeValidRole);
        RuleFor(x => x.Role).Must(r => !r.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Cannot assign Owner via add member.");
    }

    private static bool BeValidRole(string role) =>
        Enum.TryParse<MemberRole>(role, true, out _);
}

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator()
    {
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.Role).Must(r => Enum.TryParse<MemberRole>(r, true, out _));
        RuleFor(x => x.Role).Must(r => !r.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Cannot assign Owner via update member.");
    }
}
