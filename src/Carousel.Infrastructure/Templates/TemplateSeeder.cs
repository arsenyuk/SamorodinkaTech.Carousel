using System.Security.Cryptography;
using System.Text.Json;
using Carousel.Domain.Entities;
using Carousel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Templates;

public static class TemplateSeeder
{
    public static async Task SeedAsync(CarouselDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (await db.CarouselTemplates.AnyAsync(ct))
        {
            return;
        }

        // Ensure system tenant exists for template background media.
        var systemTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == CarouselTemplate.SystemTenantId, ct);
        if (systemTenant is null)
        {
            // Templates are global; system tenant has no owner. Create a dummy owner if needed.
            var owner = await db.Users.FirstOrDefaultAsync(ct);
            if (owner is null)
            {
                owner = new User
                {
                    TelegramUserId = 1,
                    TelegramUsername = "__system__",
                    DisplayName = "System",
                };
                db.Users.Add(owner);
                await db.SaveChangesAsync(ct);
            }

            systemTenant = new Tenant
            {
                Id = CarouselTemplate.SystemTenantId,
                Name = "__system__",
                OwnerUserId = owner.Id,
            };
            db.Tenants.Add(systemTenant);
            await db.SaveChangesAsync(ct);
        }

        foreach (var spec in BackgroundGenerator.Specs)
        {
            var png = BackgroundGenerator.GeneratePng(spec);
            var sha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();

            var media = new MediaAsset
            {
                TenantId = CarouselTemplate.SystemTenantId,
                ContentType = "image/png",
                SizeBytes = png.LongLength,
                Width = BackgroundGenerator.Size,
                Height = BackgroundGenerator.Size,
                StorageKey = $"pg://system/templates/{spec.Code}.png",
                Sha256 = sha,
                Content = png,
            };
            db.MediaAssets.Add(media);

            var styles = BuildDefaultStyles(spec);
            var template = new CarouselTemplate
            {
                Code = spec.Code,
                Name = spec.Name,
                Description = spec.Description,
                DefaultStylesJson = JsonSerializer.Serialize(styles),
                BackgroundMediaAssetId = media.Id,
            };
            db.CarouselTemplates.Add(template);

            logger?.LogInformation("Seeded carousel template {Code} with background media {MediaId}", spec.Code, media.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<CardStyle> BuildDefaultStyles(TemplateBackgroundSpec spec)
    {
        var styles = new List<CardStyle>();
        for (var i = 0; i < 3; i++)
        {
            styles.Add(new CardStyle
            {
                FontName = spec.DefaultStyleFonts[i],
                Color = spec.DefaultStyleColors[i],
                FontSize = spec.DefaultStyleSizes[i],
                FontStyle = spec.DefaultStyleItalics[i] == "true" ? "Italic" : "Normal",
            });
        }

        return styles;
    }
}
