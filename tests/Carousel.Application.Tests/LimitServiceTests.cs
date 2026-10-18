using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Carousel.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Carousel.Application.Tests;

public class LimitServiceTests
{
    [Fact]
    public async Task EnsureCanPublish_without_subscription_throws()
    {
        var billing = new Mock<IBillingQuery>();
        billing.Setup(b => b.GetActiveSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionSnapshot?)null);

        var service = new LimitService(billing.Object);

        var act = () => service.EnsureCanPublishAsync(Guid.NewGuid(), 3);

        await act.Should().ThrowAsync<SubscriptionRequiredException>();
    }

    [Fact]
    public async Task EnsureCanPublish_over_cards_limit_throws()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var billing = new Mock<IBillingQuery>();
        billing.Setup(b => b.GetActiveSubscriptionAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionSnapshot(Guid.NewGuid(), planId, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(30)));
        billing.Setup(b => b.GetPlanLimitsAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<LimitKey, long>
            {
                [LimitKey.CardsPerPublication] = 2,
                [LimitKey.MonthlyPublications] = 10,
            });
        billing.Setup(b => b.GetMonthlyPublicationCountAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = new LimitService(billing.Object);

        var act = () => service.EnsureCanPublishAsync(tenantId, 5);

        await act.Should().ThrowAsync<LimitExceededException>()
            .WithMessage("*plan allows 2*");
    }

    [Fact]
    public async Task EnsureCanPublish_over_monthly_limit_throws()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var billing = new Mock<IBillingQuery>();
        billing.Setup(b => b.GetActiveSubscriptionAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionSnapshot(Guid.NewGuid(), planId, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(30)));
        billing.Setup(b => b.GetPlanLimitsAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<LimitKey, long>
            {
                [LimitKey.CardsPerPublication] = 10,
                [LimitKey.MonthlyPublications] = 3,
            });
        billing.Setup(b => b.GetMonthlyPublicationCountAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var service = new LimitService(billing.Object);

        var act = () => service.EnsureCanPublishAsync(tenantId, 2);

        await act.Should().ThrowAsync<LimitExceededException>()
            .WithMessage("*limit reached (3)*");
    }

    [Fact]
    public async Task EnsureCanPublish_within_limits_succeeds()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var billing = new Mock<IBillingQuery>();
        billing.Setup(b => b.GetActiveSubscriptionAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionSnapshot(Guid.NewGuid(), planId, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(30)));
        billing.Setup(b => b.GetPlanLimitsAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<LimitKey, long>
            {
                [LimitKey.CardsPerPublication] = 10,
                [LimitKey.MonthlyPublications] = 3,
            });
        billing.Setup(b => b.GetMonthlyPublicationCountAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = new LimitService(billing.Object);

        var act = () => service.EnsureCanPublishAsync(tenantId, 5);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureCanPublish_expired_subscription_throws()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var billing = new Mock<IBillingQuery>();
        billing.Setup(b => b.GetActiveSubscriptionAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionSnapshot(Guid.NewGuid(), planId, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(-1)));
        billing.Setup(b => b.GetPlanLimitsAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<LimitKey, long>());

        var service = new LimitService(billing.Object);

        var act = () => service.EnsureCanPublishAsync(tenantId, 1);

        await act.Should().ThrowAsync<SubscriptionRequiredException>();
    }
}
