using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public static class PublicationStatusRules
{
    public static bool CanTransition(PublicationStatus from, PublicationStatus to) => (from, to) switch
    {
        (PublicationStatus.Draft, PublicationStatus.Ready) => true,
        (PublicationStatus.Ready, PublicationStatus.Publishing) => true,
        (PublicationStatus.Publishing, PublicationStatus.Published) => true,
        (PublicationStatus.Publishing, PublicationStatus.PartiallyFailed) => true,
        (PublicationStatus.Publishing, PublicationStatus.Failed) => true,
        (PublicationStatus.PartiallyFailed, PublicationStatus.Publishing) => true,
        (PublicationStatus.Failed, PublicationStatus.Publishing) => true,
        _ => false,
    };

    public static DeliveryStatus AggregateDeliveries(IEnumerable<DeliveryStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Count == 0)
        {
            return DeliveryStatus.Skipped;
        }

        if (list.All(s => s == DeliveryStatus.Succeeded))
        {
            return DeliveryStatus.Succeeded;
        }

        if (list.All(s => s is DeliveryStatus.Failed or DeliveryStatus.Skipped))
        {
            return DeliveryStatus.Failed;
        }

        if (list.Any(s => s is DeliveryStatus.Succeeded or DeliveryStatus.Pending or DeliveryStatus.InProgress))
        {
            return DeliveryStatus.Pending;
        }

        return DeliveryStatus.Failed;
    }
}
