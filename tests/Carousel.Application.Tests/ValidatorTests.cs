using Carousel.Application.DTOs;
using Carousel.Application.Validators;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class ValidatorTests
{
    [Fact]
    public void CreatePublication_rejects_empty_title()
    {
        var validator = new CreatePublicationRequestValidator();
        var result = validator.Validate(new CreatePublicationRequest("", null));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreatePublication_accepts_valid()
    {
        var validator = new CreatePublicationRequestValidator();
        var result = validator.Validate(new CreatePublicationRequest("Title", "Desc"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateCard_requires_text_or_media()
    {
        var validator = new CreateCardRequestValidator();
        var result = validator.Validate(new CreateCardRequest("  ", null, null));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Reorder_rejects_duplicates()
    {
        var validator = new ReorderCardsRequestValidator();
        var id = Guid.NewGuid();
        var result = validator.Validate(new ReorderCardsRequest([id, id]));
        result.IsValid.Should().BeFalse();
    }
}
