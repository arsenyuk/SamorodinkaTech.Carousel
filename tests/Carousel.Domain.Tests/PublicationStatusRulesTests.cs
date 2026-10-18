using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Carousel.Domain.Tests;

public class PublicationStatusRulesTests
{
    [Theory]
    [InlineData(PublicationStatus.Draft, PublicationStatus.Ready, true)]
    [InlineData(PublicationStatus.Ready, PublicationStatus.Publishing, true)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.Published, true)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.PartiallyFailed, true)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.Failed, true)]
    [InlineData(PublicationStatus.PartiallyFailed, PublicationStatus.Publishing, true)]
    [InlineData(PublicationStatus.Failed, PublicationStatus.Publishing, true)]
    [InlineData(PublicationStatus.Draft, PublicationStatus.Published, false)]
    [InlineData(PublicationStatus.Published, PublicationStatus.Publishing, false)]
    public void CanTransition_follows_state_machine(PublicationStatus from, PublicationStatus to, bool expected)
    {
        PublicationStatusRules.CanTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void AggregateDeliveries_all_succeeded()
    {
        var result = PublicationStatusRules.AggregateDeliveries(
        [
            DeliveryStatus.Succeeded,
            DeliveryStatus.Succeeded,
        ]);

        result.Should().Be(DeliveryStatus.Succeeded);
    }

    [Fact]
    public void AggregateDeliveries_mixed_success_and_fail_returns_pending()
    {
        var result = PublicationStatusRules.AggregateDeliveries(
        [
            DeliveryStatus.Succeeded,
            DeliveryStatus.Failed,
        ]);

        result.Should().Be(DeliveryStatus.Pending);
    }

    [Fact]
    public void AggregateDeliveries_all_failed()
    {
        var result = PublicationStatusRules.AggregateDeliveries(
        [
            DeliveryStatus.Failed,
            DeliveryStatus.Skipped,
        ]);

        result.Should().Be(DeliveryStatus.Failed);
    }

    [Fact]
    public void AggregateDeliveries_empty_is_skipped()
    {
        PublicationStatusRules.AggregateDeliveries([]).Should().Be(DeliveryStatus.Skipped);
    }
}
