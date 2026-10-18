using Carousel.Domain.Enums;

namespace Carousel.Domain.Entities;

public class PlanLimit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public LimitKey LimitKey { get; set; }
    public long Value { get; set; }

    public Plan Plan { get; set; } = null!;
}
