using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Carousel.Integration.Tests;

public class ApiFlowTests : IClassFixture<ApiFactory>
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly ApiFactory _factory;

    public ApiFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
/// Логин через unsigned JSON initData с username (BotToken в тестовой фабрике пуст —
/// хэш не проверяется). Уникальные tester-username не попадают в exempt;
/// exempt-кейс передаёт username: "devuser".
/// </summary>
    private async Task<(HttpClient Client, Guid TenantId, long TelegramUserId)> CreateOwnerAsync(
        string? name = null, string? username = null)
    {
        var client = _factory.CreateClient();
        var tgId = Random.Shared.NextInt64(1_000_000, long.MaxValue - 10_000);
        var user = username ?? $"tester{Random.Shared.NextInt64(10_000, 99_999)}";

        var userJson = JsonSerializer.Serialize(new { id = tgId, first_name = "Tester", username = user });
        // Поле hash обязательно в query-form; HMAC не проверяется — BotToken в фабрике пуст.
        var initData =
            $"auth_date={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" +
            $"&user={Uri.EscapeDataString(userJson)}" +
            $"&hash={new string('0', 64)}";

        var auth = await client.PostAsJsonAsync("/api/auth/telegram", new AuthTelegramRequest(initData));
        auth.EnsureSuccessStatusCode();
        var authBody = await auth.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authBody!.Token);

        var tenantResp = await client.PostAsJsonAsync("/api/tenants", new CreateTenantRequest(name ?? $"Workspace {user}"));
        tenantResp.EnsureSuccessStatusCode();
        var tenant = await tenantResp.Content.ReadFromJsonAsync<TenantDto>();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant!.Id.ToString());

        return (client, tenant.Id, tgId);
    }

    private static MultipartFormDataContent BuildPngForm(string fileName = "pixel.png", byte[]? bytes = null, string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes ?? TinyPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return form;
    }

    [Fact]
    public async Task Create_tenant_auto_free_subscription()
    {
        var (client, tenantId, _) = await CreateOwnerAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
        var sub = db.Subscriptions.AsQueryable().FirstOrDefault(s => s.TenantId == tenantId);
        sub.Should().NotBeNull();
        sub!.Status.Should().Be(Domain.Enums.SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Full_flow_ready_publish_and_retry()
    {
        var (client, _, telegramUserId) = await CreateOwnerAsync();

        // Status is detectable before publishing: not connected yet.
        var statusBefore = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/api/telegram/business-connection");
        statusBefore.GetProperty("connected").GetBoolean().Should().BeFalse();

        // Stories go through the user's business connection — register it (dev endpoint).
        var connResp = await client.PostAsJsonAsync(
            "/api/dev/telegram/business-connection",
            new { telegramUserId, username = "owner" });
        connResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

        var statusAfter = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/api/telegram/business-connection");
        statusAfter.GetProperty("connected").GetBoolean().Should().BeTrue();

        var planResp = await client.GetFromJsonAsync<List<PlanDto>>("/api/billing/plans");
        planResp.Should().NotBeNull();
        planResp!.Any(p => p.Code == "free").Should().BeTrue();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Carousel #1", "Description"));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();
        publication.Should().NotBeNull();
        publication!.Status.Should().Be("Draft");

        using (var form = BuildPngForm())
        {
            var mediaResp = await client.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            var media = await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>();
            media.Should().NotBeNull();
            media!.Width.Should().Be(1);
            media.Height.Should().Be(1);

            var cardResp = await client.PostAsJsonAsync(
                $"/api/publications/{publication.Id}/cards",
                new CreateCardRequest("Hello", media.Id, null));
            cardResp.EnsureSuccessStatusCode();
        }

        // «Другие площадки» (generic publish + platform-accounts) отключены — публикация только /publish/me.
        // Publish from Draft must be rejected until ready.
        var earlyPublish = await client.PostAsync($"/api/publications/{publication.Id}/publish/me", null);
        earlyPublish.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);

        var readyResp = await client.PostAsync($"/api/publications/{publication.Id}/ready", null);
        readyResp.EnsureSuccessStatusCode();
        var readyBody = await readyResp.Content.ReadFromJsonAsync<ReadyPublicationResponse>();
        readyBody!.Status.Should().Be("Ready");

        // 1-й publish: карусель с фото стоит 1 звезду → PaymentRequired + инвойс.
        var payResp = await client.PostAsync($"/api/publications/{publication.Id}/publish/me", null);
        payResp.EnsureSuccessStatusCode();
        var payBody = await payResp.Content.ReadFromJsonAsync<PublishResponse>();
        payBody!.Status.Should().Be("PaymentRequired");
        payBody.InvoiceUrl.Should().StartWith("https://t.me/invoice/");

        // Имитация оплаты (dev-эндпоинт, Development).
        var paid = await client.PostAsJsonAsync(
            "/api/dev/billing/stars-paid", new { publicationId = publication.Id });
        paid.EnsureSuccessStatusCode();

        // 2-й publish: оплата увидена → Published (оплаченная публикация обходит квоту).
        var publishResp = await client.PostAsync($"/api/publications/{publication.Id}/publish/me", null);
        publishResp.EnsureSuccessStatusCode();
        var publishBody = await publishResp.Content.ReadFromJsonAsync<PublishResponse>();

        publishBody.Should().NotBeNull();
        publishBody!.Status.Should().Be("Published");
        publishBody.InvoiceUrl.Should().BeNull();
        publishBody.Deliveries.Should().HaveCount(1);
        publishBody.Deliveries[0].Status.Should().Be("Succeeded");
        publishBody.Deliveries[0].ExternalPostId.Should().StartWith("tg-story-");

        // Повторный publish: оплата НЕ запрашивается повторно (гейт Paid) — падаем лишь в
        // статус-гейт «Cannot publish from status Published» (409/403 без PaymentRequired).
        var again = await client.PostAsync($"/api/publications/{publication.Id}/publish/me", null);
        var againBody = await again.Content.ReadAsStringAsync();
        again.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        againBody.Should().NotContain("PaymentRequired");
        againBody.Should().Contain("Cannot publish from status Published");

        var retryResp = await client.PostAsync($"/api/publications/{publication.Id}/publish/retry", null);
        retryResp.EnsureSuccessStatusCode();
        var retryBody = await retryResp.Content.ReadFromJsonAsync<PublishResponse>();
        retryBody!.Deliveries.Should().HaveCount(1);
        retryBody.Deliveries[0].ExternalPostId.Should().Be(publishBody.Deliveries[0].ExternalPostId);
    }

    [Fact]
    public async Task Publish_for_exempt_dev_id_skips_payment()
    {
        // "devuser" в Billing:FreeUsernames (ApiFactory) — публикация без оплаты.
        var (client, _, telegramUserId) = await CreateOwnerAsync(username: "devuser");

        // Без business connection публикация упала бы с NO_BUSINESS_CONNECTION.
        (await client.PostAsJsonAsync(
            "/api/dev/telegram/business-connection",
            new { telegramUserId, username = "exempt" }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Exempt", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        using (var form = BuildPngForm())
        {
            var mediaResp = await client.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            var media = await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>();
            var cardResp = await client.PostAsJsonAsync(
                $"/api/publications/{publication!.Id}/cards",
                new CreateCardRequest("Photo", media!.Id, null));
            cardResp.EnsureSuccessStatusCode();
        }

        (await client.PostAsync($"/api/publications/{publication.Id}/ready", null))
            .EnsureSuccessStatusCode();

        var publishResp = await client.PostAsync($"/api/publications/{publication.Id}/publish/me", null);
        publishResp.EnsureSuccessStatusCode();
        var body = await publishResp.Content.ReadFromJsonAsync<PublishResponse>();

        body!.Status.Should().Be("Published");
        body.InvoiceUrl.Should().BeNull("exempt id не должен получать инвойс");
    }

    [Fact]
    public async Task Stars_paid_endpoint_without_pending_payment_returns_404()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var resp = await client.PostAsJsonAsync(
            "/api/dev/billing/stars-paid", new { publicationId = Guid.NewGuid() });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publish_without_photo_with_noAds_requires_payment_single_star()
    {
        // Нет фото, но включён крыжек «без рекламы» → оплата; цена НЕ суммируется (1, не 2).
        var (client, _, _) = await CreateOwnerAsync();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("NoAds", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        var cardResp = await client.PostAsJsonAsync(
            $"/api/publications/{publication!.Id}/cards",
            new CreateCardRequest("Только текст", null, null));
        cardResp.EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/publications/{publication.Id}/ready", null))
            .EnsureSuccessStatusCode();

        var payResp = await client.PostAsJsonAsync(
            $"/api/publications/{publication.Id}/publish/me", new { noAds = true });
        payResp.EnsureSuccessStatusCode();
        var payBody = await payResp.Content.ReadFromJsonAsync<PublishResponse>();
        payBody!.Status.Should().Be("PaymentRequired");
        payBody.InvoiceUrl.Should().StartWith("https://t.me/invoice/");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            var payment = db.Payments.Single(p => p.IdempotencyKey == $"pub:{publication.Id}");
            payment.AmountMinor.Should().Be(1, "крыжек и фото не суммируются — цена одна");
        }

        // Флаг сохранён в публикации (влияет на рекламу в доставке и на retry).
        var after = await client.GetFromJsonAsync<PublicationDto>($"/api/publications/{publication.Id}");
        after!.NoAds.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_photo_with_noAds_pays_single_star_not_two()
    {
        var (client, _, _) = await CreateOwnerAsync();
        var pubId = await CreateReadyPhotoPublicationAsync(client);

        var payResp = await client.PostAsJsonAsync(
            $"/api/publications/{pubId}/publish/me", new { noAds = true });
        payResp.EnsureSuccessStatusCode();
        (await payResp.Content.ReadFromJsonAsync<PublishResponse>())!.Status.Should().Be("PaymentRequired");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            var payment = db.Payments.Single(p => p.IdempotencyKey == $"pub:{pubId}");
            payment.AmountMinor.Should().Be(1, "оба условия ≠ 2 звезды");
        }
    }

    private async Task<Guid> CreateReadyPhotoPublicationAsync(HttpClient client)
    {
        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Refund flow", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        using (var form = BuildPngForm())
        {
            var mediaResp = await client.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            var media = await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>();
            var cardResp = await client.PostAsJsonAsync(
                $"/api/publications/{publication!.Id}/cards",
                new CreateCardRequest("Photo", media!.Id, null));
            cardResp.EnsureSuccessStatusCode();
        }

        (await client.PostAsync($"/api/publications/{publication.Id}/ready", null))
            .EnsureSuccessStatusCode();
        return publication.Id;
    }

    [Fact]
    public async Task Failed_publish_refunds_stars_and_requires_new_payment()
    {
        // НЕТ business connection → фундаментальный фейл NO_BUSINESS_CONNECTION после оплаты.
        var (client, _, _) = await CreateOwnerAsync();
        var pubId = await CreateReadyPhotoPublicationAsync(client);

        var payResp = await client.PostAsync($"/api/publications/{pubId}/publish/me", null);
        payResp.EnsureSuccessStatusCode();
        var payBody = await payResp.Content.ReadFromJsonAsync<PublishResponse>();
        payBody!.Status.Should().Be("PaymentRequired");
        var firstInvoice = payBody.InvoiceUrl;
        firstInvoice.Should().StartWith("https://t.me/invoice/");

        (await client.PostAsJsonAsync(
            "/api/dev/billing/stars-paid", new { publicationId = pubId }))
            .EnsureSuccessStatusCode();

        // Публикация невозможна → отказ + возврат звёзд.
        var fail = await client.PostAsync($"/api/publications/{pubId}/publish/me", null);
        fail.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
        var failText = await fail.Content.ReadAsStringAsync();
        failText.Should().Contain("NO_BUSINESS_CONNECTION");
        failText.Should().Contain("Звёзды возвращены");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            var payment = db.Payments.Single(p => p.IdempotencyKey == $"pub:{pubId}");
            payment.Status.Should().Be(Domain.Enums.PaymentStatus.Refunded);
        }

        // Новый цикл: publish снова требует оплату, ссылка СВЕЖАЯ.
        var again = await client.PostAsync($"/api/publications/{pubId}/publish/me", null);
        again.EnsureSuccessStatusCode();
        var againBody = await again.Content.ReadFromJsonAsync<PublishResponse>();
        againBody!.Status.Should().Be("PaymentRequired");
        againBody.InvoiceUrl.Should().NotBe(firstInvoice);

        // Retry после refund тоже требует новой оплаты — не публикуем бесплатно.
        var retry = await client.PostAsync($"/api/publications/{pubId}/publish/retry", null);
        retry.EnsureSuccessStatusCode();
        (await retry.Content.ReadFromJsonAsync<PublishResponse>())!.Status.Should().Be("PaymentRequired");
    }

    [Fact]
    public async Task Transient_story_failure_keeps_payment_completed()
    {
        // С подключённым business connection, но падающим postStory (транзиентный HTTP) —
        // возврата НЕ должно быть, retry остаётся бесплатным.
        var (client, _, telegramUserId) = await CreateOwnerAsync();
        (await client.PostAsJsonAsync(
            "/api/dev/telegram/business-connection",
            new { telegramUserId, username = "owner" }))
            .EnsureSuccessStatusCode();

        var mock = _factory.Services.GetRequiredService<Infrastructure.Bot.MockTelegramBotApi>();
        mock.StoryErrorToReturn = Carousel.Application.Abstractions.TelegramApiResult.Fail("HTTP", "timeout");
        try
        {
            var pubId = await CreateReadyPhotoPublicationAsync(client);

            var payResp = await client.PostAsync($"/api/publications/{pubId}/publish/me", null);
            payResp.EnsureSuccessStatusCode();
            (await payResp.Content.ReadFromJsonAsync<PublishResponse>())!.Status.Should().Be("PaymentRequired");

            (await client.PostAsJsonAsync(
                "/api/dev/billing/stars-paid", new { publicationId = pubId }))
                .EnsureSuccessStatusCode();

            var fail = await client.PostAsync($"/api/publications/{pubId}/publish/me", null);
            fail.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
            (await fail.Content.ReadAsStringAsync()).Should().NotContain("Звёзды возвращены");

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
                var payment = db.Payments.Single(p => p.IdempotencyKey == $"pub:{pubId}");
                payment.Status.Should().Be(Domain.Enums.PaymentStatus.Completed);
            }
        }
        finally
        {
            mock.StoryErrorToReturn = null;
        }
    }

    [Fact]
    public async Task Templates_list_has_eight_and_apply_sets_template_id()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var templates = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/templates");
        templates.Should().NotBeNull();
        templates!.Count.Should().Be(8);
        foreach (var t in templates)
        {
            t.GetProperty("backgroundMediaAssetId").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
        }

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Tpl", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        var templateId = templates[0].GetProperty("id").GetGuid();
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/publications/{publication!.Id}",
            new { templateId });
        patchResp.EnsureSuccessStatusCode();
        var patched = await patchResp.Content.ReadFromJsonAsync<PublicationDto>();
        patched!.TemplateId.Should().Be(templateId);

        var bgId = templates[0].GetProperty("backgroundMediaAssetId").GetGuid();
        var bg = await client.GetAsync($"/api/templates/{templates[0].GetProperty("id").GetGuid()}/background");
        bg.EnsureSuccessStatusCode();
        bg.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        _ = bgId;
    }

    [Fact]
    public async Task Patch_clear_template_removes_applied_template()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var templates = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/templates");
        var templateId = templates![0].GetProperty("id").GetGuid();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Clear tpl", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        var applyResp = await client.PatchAsJsonAsync(
            $"/api/publications/{publication!.Id}", new { templateId });
        applyResp.EnsureSuccessStatusCode();
        (await applyResp.Content.ReadFromJsonAsync<PublicationDto>())!.TemplateId.Should().Be(templateId);

        var clearResp = await client.PatchAsJsonAsync(
            $"/api/publications/{publication.Id}", new { clearTemplate = true });
        clearResp.EnsureSuccessStatusCode();
        (await clearResp.Content.ReadFromJsonAsync<PublicationDto>())!.TemplateId.Should().BeNull();
    }

    [Fact]
    public async Task Delete_last_card_resets_template()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var templates = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/templates");
        var templateId = templates![0].GetProperty("id").GetGuid();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Del last", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        (await client.PatchAsJsonAsync(
            $"/api/publications/{publication!.Id}", new { templateId }))
            .EnsureSuccessStatusCode();

        var cardResp = await client.PostAsJsonAsync(
            $"/api/publications/{publication.Id}/cards",
            new CreateCardRequest("Единственная", null, null));
        cardResp.EnsureSuccessStatusCode();
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();

        // Удаление единственной карточки = пустая карусель → шаблон сбрасывается.
        (await client.DeleteAsync($"/api/publications/{publication.Id}/cards/{card!.Id}"))
            .EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<PublicationDto>($"/api/publications/{publication.Id}");
        after!.Cards.Should().BeEmpty();
        after.TemplateId.Should().BeNull("удаление последней карточки обязано сбрасывать шаблон, как bulk-clear");
    }

    [Fact]
    public async Task Publish_text_only_carousel_renders_story_image()
    {
        var (client, _, telegramUserId) = await CreateOwnerAsync();

        (await client.PostAsJsonAsync(
            "/api/dev/telegram/business-connection",
            new { telegramUserId, username = "owner" }))
            .EnsureSuccessStatusCode();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Text only", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        var cardResp = await client.PostAsJsonAsync(
            $"/api/publications/{publication!.Id}/cards",
            new CreateCardRequest("Просто текст, без фото", null, null));
        cardResp.EnsureSuccessStatusCode();

        (await client.PostAsync($"/api/publications/{publication.Id}/ready", null))
            .EnsureSuccessStatusCode();

        var publishResp = await client.PostAsync($"/api/publications/{publication.Id}/publish/me", null);
        publishResp.EnsureSuccessStatusCode();
        var body = await publishResp.Content.ReadFromJsonAsync<PublishResponse>();

        body!.Status.Should().Be("Published",
            "текстовая карточка должна рендериться в PNG 1080×1920, а не давать NO_CONTENT");
        body.Deliveries.Should().HaveCount(1);
        body.Deliveries[0].Status.Should().Be("Succeeded");
        body.Deliveries[0].ExternalPostId.Should().StartWith("tg-story-");

        // Mock outbox: ушло как изображение (rendered PNG), без caption-дубля текста.
        var outbox = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/dev/telegram/outbox");
        outbox.Should().NotBeNull();
        outbox!.Any(o => o.GetProperty("text").GetString()!.Contains("image/png"))
            .Should().BeTrue("рендеренная карточка должна уйти в outbox как image/png");
    }

    [Fact]
    public async Task Story_image_endpoint_returns_publish_bytes()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Preview", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        // Текстовая карточка → серверный PNG 1080×1920 (превью = publish-байты).
        var textCard = await client.PostAsJsonAsync(
            $"/api/publications/{publication!.Id}/cards",
            new CreateCardRequest("Текст для превью", null, null));
        textCard.EnsureSuccessStatusCode();
        var textCardDto = await textCard.Content.ReadFromJsonAsync<CardDto>();

        var preview = await client.GetAsync($"/api/publications/{publication.Id}/cards/{textCardDto!.Id}/story-image");
        preview.EnsureSuccessStatusCode();
        preview.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var png = await preview.Content.ReadAsByteArrayAsync();
        png[0].Should().Be(0x89, "ожидается PNG (рендер 1080×1920)");
        png[1].Should().Be((byte)'P');

        // Фото-карточка → исходные байты медиа (с печатью ссылки при !NoAds — формат сохраняется).
        Guid mediaId;
        using (var form = BuildPngForm())
        {
            var mediaResp = await client.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            mediaId = (await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>())!.Id;
        }

        var photoCard = await client.PostAsJsonAsync(
            $"/api/publications/{publication.Id}/cards",
            new CreateCardRequest("Фото", mediaId, null));
        photoCard.EnsureSuccessStatusCode();
        var photoCardDto = await photoCard.Content.ReadFromJsonAsync<CardDto>();

        var photoPreview = await client.GetAsync(
            $"/api/publications/{publication.Id}/cards/{photoCardDto!.Id}/story-image");
        photoPreview.EnsureSuccessStatusCode();
        (await photoPreview.Content.ReadAsByteArrayAsync())[0].Should().Be(0x89);

        // Пустая карточка (нет текста и медиа) → 400.
        var emptyCard = await client.PostAsJsonAsync(
            $"/api/publications/{publication.Id}/cards",
            new CreateCardRequest(null, null, null));
        // валидация создания может сама отклонить пустую карточку — тогда 400 уже здесь
        if (emptyCard.IsSuccessStatusCode)
        {
            var emptyDto = await emptyCard.Content.ReadFromJsonAsync<CardDto>();
            var bad = await client.GetAsync(
                $"/api/publications/{publication.Id}/cards/{emptyDto!.Id}/story-image");
            bad.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }
        else
        {
            emptyCard.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Cleanup_removes_stale_publications_keeps_recent_and_publishing()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var staleId = await CreatePublicationWithCardAsync(client, "Stale 200d");
        var freshId = await CreatePublicationWithCardAsync(client, "Fresh");
        var publishingId = await CreatePublicationWithCardAsync(client, "In flight");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            var old = DateTime.UtcNow.AddDays(-200);
            db.Publications.Single(p => p.Id == staleId).CreatedAtUtc = old;
            var inFlight = db.Publications.Single(p => p.Id == publishingId);
            inFlight.CreatedAtUtc = old;
            inFlight.Status = Domain.Enums.PublicationStatus.Publishing;
            await db.SaveChangesAsync();
        }

        var report = await RunCleanupAsync();
        // Publishing исключён из чистки → удаляется только stale.
        report.PublicationsRemoved.Should().Be(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            db.Publications.Any(p => p.Id == staleId).Should().BeFalse("устаревшая публикация удаляется");
            db.Cards.Any(c => c.PublicationId == staleId).Should().BeFalse("карточки уходят каскадом");
            db.Publications.Any(p => p.Id == freshId).Should().BeTrue("свежая публикация живёт");
            db.Publications.Any(p => p.Id == publishingId).Should().BeTrue("Publishing в полёте не трогаем");
        }
    }

    [Fact]
    public async Task Cleanup_removes_orphan_media_but_keeps_linked_and_fresh()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var pubId = await CreatePublicationWithCardAsync(client, "Media owner");

        // Важно: разные байты — сервер дедуплицирует медиа по sha256.
        var orphanBytes = Infrastructure.Templates.BackgroundGenerator.GenerateVerticalGradientPng(
            SkiaSharp.SKColors.Red, SkiaSharp.SKColors.Blue, 2);
        var linkedBytes = Infrastructure.Templates.BackgroundGenerator.GenerateVerticalGradientPng(
            SkiaSharp.SKColors.Green, SkiaSharp.SKColors.White, 2);
        var freshBytes = Infrastructure.Templates.BackgroundGenerator.GenerateVerticalGradientPng(
            SkiaSharp.SKColors.Yellow, SkiaSharp.SKColors.Black, 2);

        Guid orphanId;
        Guid linkedId;
        Guid freshOrphanId;
        using (var form = BuildPngForm("orphan.png", orphanBytes))
        {
            var resp = await client.PostAsync("/api/media", form);
            resp.EnsureSuccessStatusCode();
            orphanId = (await resp.Content.ReadFromJsonAsync<MediaAssetDto>())!.Id;
        }

        using (var form = BuildPngForm("linked.png", linkedBytes))
        {
            var resp = await client.PostAsync("/api/media", form);
            resp.EnsureSuccessStatusCode();
            linkedId = (await resp.Content.ReadFromJsonAsync<MediaAssetDto>())!.Id;
        }

        using (var form = BuildPngForm("fresh-orphan.png", freshBytes))
        {
            var resp = await client.PostAsync("/api/media", form);
            resp.EnsureSuccessStatusCode();
            freshOrphanId = (await resp.Content.ReadFromJsonAsync<MediaAssetDto>())!.Id;
        }

        orphanId.Should().NotBe(linkedId, "медиа с разным содержимым не должны дедуплицироваться");

        // Привязать linked к карточке.
        var cardResp = await client.PostAsJsonAsync(
            $"/api/publications/{pubId}/cards",
            new CreateCardRequest("С картинкой", linkedId, null));
        cardResp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            var old = DateTime.UtcNow.AddDays(-40);
            db.MediaAssets.Single(m => m.Id == orphanId).CreatedAtUtc = old;
            db.MediaAssets.Single(m => m.Id == linkedId).CreatedAtUtc = old;
            await db.SaveChangesAsync();
        }

        var report = await RunCleanupAsync();
        report.MediaAssetsRemoved.Should().Be(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            db.MediaAssets.Any(m => m.Id == orphanId).Should().BeFalse("осиротевшее старое медиа удаляется");
            db.MediaAssets.Any(m => m.Id == linkedId).Should().BeTrue("привязанное к карточке живёт");
            db.MediaAssets.Any(m => m.Id == freshOrphanId).Should().BeTrue("свежее (<30 дней) живёт");
        }
    }

    [Fact]
    public async Task Cleanup_keeps_completed_payments_when_publication_removed()
    {
        var (client, _, _) = await CreateOwnerAsync();
        var pubId = await CreateReadyPhotoPublicationAsync(client);

        (await client.PostAsync($"/api/publications/{pubId}/publish/me", null))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            "/api/dev/billing/stars-paid", new { publicationId = pubId }))
            .EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            db.Publications.Single(p => p.Id == pubId).CreatedAtUtc = DateTime.UtcNow.AddDays(-200);
            await db.SaveChangesAsync();
        }

        var report = await RunCleanupAsync();
        report.PublicationsRemoved.Should().BeGreaterThanOrEqualTo(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
            db.Publications.Any(p => p.Id == pubId).Should().BeFalse("оплаченная публикация старше retention удаляется");
            var payment = db.Payments.Single(p => p.IdempotencyKey == $"pub:{pubId}");
            payment.Status.Should().Be(Domain.Enums.PaymentStatus.Completed,
                "платежи не зависят от публикации — финансовая история сохраняется");
        }
    }

    private async Task<Guid> CreatePublicationWithCardAsync(HttpClient client, string title)
    {
        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest(title, null));
        pubResp.EnsureSuccessStatusCode();
        var pub = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        var cardResp = await client.PostAsJsonAsync(
            $"/api/publications/{pub!.Id}/cards",
            new CreateCardRequest("Текст карточки", null, null));
        cardResp.EnsureSuccessStatusCode();
        return pub.Id;
    }

    private async Task<CleanupReport> RunCleanupAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IDataCleanupService>();
        return await cleanup.RunOnceAsync();
    }

    [Fact]
    public async Task Audit_entry_carries_client_ip_from_forwarded_header()
    {
        var (client, _, _) = await CreateOwnerAsync();
        // TestServer не задаёт RemoteIpAddress — IP приходит через ForwardedHeaders (XFF).
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.55");

        var pubResp = await client.PostAsJsonAsync(
            "/api/publications", new CreatePublicationRequest("Audit ip", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CarouselDbContext>();
        var entry = db.AuditEvents.AsQueryable()
            .Single(e => e.Action == "PUBLICATION:CREATED"
                && e.DetailsJson.Contains(publication!.Id.ToString()));

        entry.UserIp.Should().Be("203.0.113.55",
            "UserIp должен заполняться из X-Forwarded-For, а не оставаться unknown");
        entry.Login.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Admin_stats_forbidden_for_non_admin()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var resp = await client.GetAsync("/api/admin/stats?days=7");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_stats_returns_global_data_for_admin()
    {
        // "admintester" в Admin:Usernames (ApiFactory).
        var (client, _, _) = await CreateOwnerAsync(username: "admintester");

        var pubResp = await client.PostAsJsonAsync(
            "/api/publications", new CreatePublicationRequest("Admin stats pub", null));
        pubResp.EnsureSuccessStatusCode();

        var resp = await client.GetAsync("/api/admin/stats?days=30");
        resp.EnsureSuccessStatusCode();
        var stats = await resp.Content.ReadFromJsonAsync<AdminStatsDto>();
        stats!.Days.Should().Be(30);
        stats.Totals.Publications.Should().BeGreaterThan(0);
        stats.DayStats.Should().NotBeEmpty();
        stats.DayStats.Sum(d => d.Publications).Should().BeGreaterThanOrEqualTo(1);
        stats.ByCardCount.Should().HaveCount(7);
        stats.Counters.Should().NotBeNull();

        // days клампится: 999 → 365.
        var clamped = await client.GetFromJsonAsync<AdminStatsDto>("/api/admin/stats?days=999");
        clamped!.Days.Should().Be(365);
    }

    [Fact]
    public async Task Clear_cards_removes_all_and_resets_to_draft_and_template()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Clear me", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        // Шаблон выбран ДО очистки — очистка обязана его сбросить.
        var templates = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/templates");
        var templateId = templates![0].GetProperty("id").GetGuid();
        (await client.PatchAsJsonAsync(
            $"/api/publications/{publication!.Id}", new { templateId }))
            .EnsureSuccessStatusCode();

        using (var form = BuildPngForm())
        {
            var mediaResp = await client.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            var media = await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>();

            for (var i = 0; i < 2; i++)
            {
                var cardResp = await client.PostAsJsonAsync(
                    $"/api/publications/{publication.Id}/cards",
                    new CreateCardRequest($"Card {i}", media!.Id, null));
                cardResp.EnsureSuccessStatusCode();
            }
        }

        var readyResp = await client.PostAsync($"/api/publications/{publication.Id}/ready", null);
        readyResp.EnsureSuccessStatusCode();

        var clearResp = await client.DeleteAsync($"/api/publications/{publication.Id}/cards");
        clearResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<PublicationDto>($"/api/publications/{publication.Id}");
        after!.Cards.Should().BeEmpty();
        after.Status.Should().Be("Draft");
        after.TemplateId.Should().BeNull("очистка карточек обязана сбрасывать выбранный шаблон");
    }

    [Fact]
    public async Task Patch_card_remove_image_clears_media_and_restores_it()
    {
        var (client, _, _) = await CreateOwnerAsync();

        var pubResp = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Remove image", null));
        pubResp.EnsureSuccessStatusCode();
        var publication = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

        Guid mediaId;
        using (var form = BuildPngForm())
        {
            var mediaResp = await client.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            mediaId = (await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>())!.Id;
        }

        var cardResp = await client.PostAsJsonAsync(
            $"/api/publications/{publication!.Id}/cards",
            new CreateCardRequest("With photo", mediaId, null));
        cardResp.EnsureSuccessStatusCode();
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();
        card!.MediaAssetId.Should().Be(mediaId);

        var removeResp = await client.PatchAsJsonAsync(
            $"/api/publications/{publication.Id}/cards/{card.Id}",
            new { removeImage = true });
        removeResp.EnsureSuccessStatusCode();
        var afterRemove = await removeResp.Content.ReadFromJsonAsync<CardDto>();
        afterRemove!.MediaAssetId.Should().BeNull();

        var restoreResp = await client.PatchAsJsonAsync(
            $"/api/publications/{publication.Id}/cards/{card.Id}",
            new { mediaAssetId = mediaId });
        restoreResp.EnsureSuccessStatusCode();
        (await restoreResp.Content.ReadFromJsonAsync<CardDto>())!.MediaAssetId.Should().Be(mediaId);
    }

    [Fact]
    public async Task Media_rejects_fake_png_and_requires_multipart()
    {
        var (client, _, _) = await CreateOwnerAsync();

        using (var form = BuildPngForm("fake.png", "this is not an image"u8.ToArray()))
        {
            var resp = await client.PostAsync("/api/media", form);
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }

        var badRequest = await client.PostAsJsonAsync("/api/media", new { });
        badRequest.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        var telegramUserId = Random.Shared.NextInt64(1_000_000, long.MaxValue - 10_000);
        client.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramUserId.ToString());
        var auth = await client.PostAsJsonAsync("/api/auth/telegram", new AuthTelegramRequest(telegramUserId.ToString()));
        auth.EnsureSuccessStatusCode();
        var authBody = await auth.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authBody!.Token);

        // Create membership first so missing header is distinguished from no-workspace.
        var tenantResp = await client.PostAsJsonAsync("/api/tenants", new CreateTenantRequest("WS"));
        tenantResp.EnsureSuccessStatusCode();

        var pub = await client.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("X", null));
        pub.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Viewer_can_read_but_cannot_mutate()
    {
        var (ownerClient, tenantId, ownerTg) = await CreateOwnerAsync();

        // Second user authenticates and gets a token.
        var viewerTg = Random.Shared.NextInt64(1_000_000, long.MaxValue - 10_000);
        var viewerClient = _factory.CreateClient();
        viewerClient.DefaultRequestHeaders.Add("X-Telegram-User-Id", viewerTg.ToString());
        var viewerAuth = await viewerClient.PostAsJsonAsync("/api/auth/telegram", new AuthTelegramRequest(viewerTg.ToString()));
        viewerAuth.EnsureSuccessStatusCode();
        var viewerAuthBody = await viewerAuth.Content.ReadFromJsonAsync<AuthResponse>();
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerAuthBody!.Token);

        // Owner adds viewer.
        var add = await ownerClient.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/members",
            new AddMemberRequest(viewerTg, "Viewer"));
        add.EnsureSuccessStatusCode();

        // Viewer reads publications (with X-Tenant-Id).
        viewerClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        var list = await viewerClient.GetAsync("/api/publications");
        list.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Viewer cannot create publication.
        var create = await viewerClient.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("Nope", null));
        create.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

        // Viewer cannot manage members.
        var members = await viewerClient.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/members",
            new AddMemberRequest(ownerTg, "Editor"));
        members.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_media_attach_returns_403()
    {
        var (clientA, _, _) = await CreateOwnerAsync("A");
        var (clientB, _, _) = await CreateOwnerAsync("B");

        // Upload media to tenant B.
        using (var form = BuildPngForm())
        {
            var mediaResp = await clientB.PostAsync("/api/media", form);
            mediaResp.EnsureSuccessStatusCode();
            var mediaB = await mediaResp.Content.ReadFromJsonAsync<MediaAssetDto>();

            // Publication in tenant A tries to attach B's media.
            var pubResp = await clientA.PostAsJsonAsync("/api/publications", new CreatePublicationRequest("A", null));
            pubResp.EnsureSuccessStatusCode();
            var pubA = await pubResp.Content.ReadFromJsonAsync<PublicationDto>();

            var card = await clientA.PostAsJsonAsync(
                $"/api/publications/{pubA!.Id}/cards",
                new CreateCardRequest("text", mediaB!.Id, null));
            card.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        resp.EnsureSuccessStatusCode();
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>, Xunit.IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("carousel")
        .WithUsername("carousel")
        .WithPassword("carousel")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Carousel", _postgres.GetConnectionString());
        builder.UseSetting("Environment", "Development");
        // Exempt username для теста Publish_for_exempt — не зависит от локальных файлов конфига.
        builder.UseSetting("Billing:FreeUsernames__0", "devuser");
        // Админ для Admin_stats_*: env-слой ниже appsettings.*.Local.json, но индекс 1
        // уникален (Local занимает только индекс 0) — ключ не перекрывается.
        Environment.SetEnvironmentVariable("Admin__Usernames__1", "admintester");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
