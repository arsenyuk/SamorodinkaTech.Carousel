using Carousel.Domain.Entities;
using Carousel.Domain.Enums;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(CarouselDbContext db, CancellationToken ct = default)
    {
        if (await db.Plans.AnyAsync(ct))
        {
            return;
        }

        var free = new Plan
        {
            Code = "free",
            Name = "Free",
            PriceMinor = 0,
            Currency = "USD",
            IsActive = true,
            Limits =
            [
                new PlanLimit { LimitKey = LimitKey.MonthlyPublications, Value = 3 },
                new PlanLimit { LimitKey = LimitKey.CardsPerPublication, Value = 5 },
                new PlanLimit { LimitKey = LimitKey.MaxImageSizeMb, Value = 5 },
                new PlanLimit { LimitKey = LimitKey.MaxImageDimensionPx, Value = 2048 },
            ],
        };

        var pro = new Plan
        {
            Code = "pro",
            Name = "Pro",
            PriceMinor = 1990,
            Currency = "USD",
            IsActive = true,
            Limits =
            [
                new PlanLimit { LimitKey = LimitKey.MonthlyPublications, Value = 100 },
                new PlanLimit { LimitKey = LimitKey.CardsPerPublication, Value = 10 },
                new PlanLimit { LimitKey = LimitKey.MaxImageSizeMb, Value = 10 },
                new PlanLimit { LimitKey = LimitKey.MaxImageDimensionPx, Value = 4096 },
            ],
        };

        db.Plans.AddRange(free, pro);
        await db.SaveChangesAsync(ct);
    }
}
