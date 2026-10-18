using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Carousel.Infrastructure.Audit;
using Carousel.Infrastructure.Auth;
using Carousel.Infrastructure.Billing;
using Carousel.Infrastructure.Bot;
using Carousel.Infrastructure.Images;
using Carousel.Infrastructure.Instagram;
using Carousel.Infrastructure.Persistence;
using Carousel.Infrastructure.Publishing;
using Carousel.Infrastructure.Storage;
using Carousel.Infrastructure.Templates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // H6: no hardcoded password fallback in Production. Dev keeps localhost default for convenience.
        var connectionString = configuration.GetConnectionString("Carousel");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (environment.IsDevelopment())
            {
                connectionString = "Host=localhost;Port=5432;Database=carousel;Username=carousel;Password=carousel";
            }
            else
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Carousel is required in Production. Set via env: ConnectionStrings__Carousel");
            }
        }

        var botToken = configuration["Telegram:BotToken"] ?? string.Empty;
        var botUsername = configuration["Telegram:BotUsername"];
        var jwtKey = configuration["Auth:Jwt:SigningKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(jwtKey) && environment.IsDevelopment())
        {
            // Local development only; Production requires Auth:Jwt:SigningKey (env Auth__Jwt__SigningKey).
            jwtKey = "dev-only-signing-key-change-me-32bytes!";
        }
        var allowDevInitData = configuration.GetValue("Auth:AllowDevInitData", environment.IsDevelopment());
        var miniAppUrl = configuration["Telegram:MiniAppUrl"] ?? "http://localhost:5173";
        var useMockBot = configuration.GetValue(
            "Telegram:UseMock",
            environment.IsDevelopment() && string.IsNullOrEmpty(botToken));

        services.AddDbContext<CarouselDbContext>(options => options.UseNpgsql(connectionString));

        services.AddSingleton<IJwtTokenService>(sp => new JwtTokenService(
            jwtKey,
            sp.GetRequiredService<ILogger<JwtTokenService>>()));
        services.AddSingleton<IInitDataValidator>(_ =>
            new TelegramInitDataValidator(botToken, allowDevInitData, environment.IsDevelopment()));

        if (useMockBot)
        {
            services.AddSingleton(_ => new MockTelegramBotApi(botUsername));
            services.AddSingleton<ITelegramBotApi>(sp => sp.GetRequiredService<MockTelegramBotApi>());
        }
        else
        {
            services.AddSingleton<ITelegramBotApi>(sp =>
                new TelegramBotApi(
                    new HttpClient(),
                    sp.GetRequiredService<ILogger<TelegramBotApi>>(),
                    botToken,
                    botUsername));
        }

        services.AddHostedService(sp => new TelegramLongPollingService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ITelegramBotApi>(),
            sp.GetRequiredService<ILogger<TelegramLongPollingService>>(),
            miniAppUrl));

        // Retention-очистка БД (фоновый джоб): конфиг валидируется fail-fast (см. Program.cs).
        services.AddSingleton(_ => CleanupOptions.Validate(
            configuration.GetValue<bool>("Cleanup:Enabled"),
            configuration["Cleanup:PublicationRetentionDays"],
            configuration["Cleanup:OrphanMediaRetentionDays"],
            configuration["Cleanup:IntervalHours"]));
        services.AddScoped<IDataCleanupService, Cleanup.DataCleanupService>();
        services.AddHostedService(sp => new Cleanup.DataCleanupHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<CleanupOptions>(),
            sp.GetRequiredService<IWebHostEnvironment>(),
            sp.GetRequiredService<ILogger<Cleanup.DataCleanupHostedService>>()));

        services.AddScoped<IMediaStorage, PostgresByteaMediaStorage>();
        services.AddSingleton<ITemplateBackgroundRenderer, TemplateBackgroundRenderer>();
        // Рендер текстовых карточек в PNG 1080×1920 для postStory (шаблон + текст → изображение).
        services.AddSingleton<IStoryCardRenderer, StoryCardRenderer>();
        // Единый источник байтов карточки для публикации и режима «Превью».
        services.AddScoped<IStoryImageBuilder, StoryImageBuilder>();
        // H2 fix: HeaderImageProbe reads dimensions from file headers without full pixel decode —
        // prevents decompression-bomb OOM. No external imaging library involved.
        services.AddScoped<IImageProbe, HeaderImageProbe>();
        services.AddScoped<ITelegramAuthService, TelegramAuthService>();
        services.AddScoped<TelegramBusinessConnectionService>();
        services.AddScoped<ITelegramBusinessConnectionStore>(sp => sp.GetRequiredService<TelegramBusinessConnectionService>());
        services.AddScoped<ITelegramPublisher, BotTelegramPublisher>();
        services.AddScoped<IPublishCoordinator, PublishCoordinator>();
        services.AddScoped<IWebPaymentProvider, StubWebBillingProvider>();
        services.AddScoped<ITelegramStarsBillingProvider, StubStarsBillingProvider>();
        services.AddScoped<IBotConversationEngine, BotConversationEngine>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IBillingQuery, EfBillingQuery>();
        // Stars за публикации: цена и exempt-список валидируются fail-fast (см. Program.cs).
        services.AddSingleton(_ => StarsBillingOptions.Validate(
            configuration["Billing:StarsPrice"],
            configuration.GetSection("Billing:FreeUsernames").GetChildren().Select(c => c.Value)));
        services.AddScoped<IPublicationStarsPaymentService, TelegramStarsPaymentService>();
        // Администраторы по алиасам: валидация fail-fast (см. Program.cs).
        services.AddSingleton(_ => AdminOptions.Validate(
            configuration.GetSection("Admin:Usernames").GetChildren().Select(c => c.Value)));
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<ITenantAccessService, TenantAccessService>();

        // Stage 4: Instagram adapter — mock in Development by default, Graph API otherwise.
        var useMockInstagram = configuration.GetValue(
            "Instagram:UseMock",
            environment.IsDevelopment());
        var instagramGraphApiBase = configuration["Instagram:GraphApiBaseUrl"]
            ?? "https://graph.facebook.com/v21.0";
        var instagramAccessToken = configuration["Instagram:AccessToken"] ?? string.Empty;
        var instagramPublicMediaBaseUrl = configuration["Instagram:PublicMediaBaseUrl"]
            ?? "http://localhost:5050";

        if (useMockInstagram)
        {
            services.AddSingleton<InstagramMockOutbox>();
            services.AddScoped<IInstagramPublisher, MockInstagramPublisher>();
        }
        else
        {
            services.AddScoped<IInstagramPublisher>(sp => new InstagramGraphApiPublisher(
                new HttpClient { BaseAddress = new Uri(instagramGraphApiBase) },
                sp.GetRequiredService<ILogger<InstagramGraphApiPublisher>>(),
                instagramPublicMediaBaseUrl,
                instagramAccessToken));
        }

        // L8: persist keys to a known path so they survive container redeploy.
        // Override via env: DataProtection__KeyPath. Default: ./keys under app content root.
        var keyPath = configuration["DataProtection:KeyPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(keyPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .SetApplicationName("Carousel");

        services.AddHealthChecks()
            .AddNpgSql(connectionString);

        return services;
    }
}
