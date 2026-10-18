# Архитектура (Stage 4)

Единый модульный монолит SaaS-каруселей: создание каруселей из загруженных изображений и публикация в Telegram-каналы и Instagram (Business/Creator).

## Зафиксированные решения

| Область | Решение |
| --- | --- |
| Регистрация | Только через Telegram |
| Оплата | Telegram Stars и веб-провайдер, единый биллинг |
| Оформление | Загрузка готовых изображений |
| Instagram | Business / Creator, официальный API Meta |
| Создание | Диалог с Telegram-ботом (long polling) |
| Предпросмотр | Telegram Mini App |
| Публикация | В аккаунты заказчиков |
| Монетизация | Тарифы, лимиты, серверная проверка |
| Отложенная публикация | Не в MVP |
| Мультитенантность | Явный `X-Tenant-Id`, membership-check |
| RBAC | Owner / Editor / Viewer |
| Валидация медиа | HeaderImageProbe (magic bytes + габариты из заголовков, без decode) |
| Auth | HMAC initData + JWT (HS256, 24h) |
| Bot transport | Long polling (`getUpdates`); no-op без `Telegram:BotToken` |

## Слои

```
Carousel.Api            → Endpoints/*.cs, ProblemDetails, CORS, rate limits
Carousel.Application    → DTO, validators, JWT, initData HMAC, MediaValidation
Carousel.Domain         → entities, enums, status rules
Carousel.Infrastructure → EF + PostgreSQL, SkiaSharp (фоны шаблонов), Bot long polling, TenantAccess
src/frontends/mini-app  → React + Vite (login, list, preview, publish)
```

## Мультитенантность и RBAC (Stage 2)

- Каждый запрос workspace-скоупа обязан нести `X-Tenant-Id`; silent-fallback на «старейший» tenant **удалён**.
- Участник проверяется через `ITenantAccessService` (`GetMembershipAsync` / `EnsureMemberAsync`).
- Роли (меньшее значение = выше привилегия):

| Действие | Viewer | Editor | Owner |
| --- | --- | --- | --- |
| Чтение публикаций, media, plans | ✓ | ✓ | ✓ |
| CRUD публикаций/карточек/media | ✗ | ✓ | ✓ |
| Publish / retry / ready | ✗ | ✓ | ✓ |
| Platform accounts, billing, members | ✗ | ✗ | ✓ |

### Endpoint'ы участников

- `GET/POST /api/tenants/{id}/members`
- `PATCH/DELETE /api/tenants/{id}/members/{memberId}`

Owner нельзя назначить/снять через member-API.

## Аутентификация (Stage 3)

- `POST /api/auth/telegram` принимает **Telegram Mini App initData**.
- **Production:** HMAC-SHA256 по `data_check_string` + bot token; `auth_date` ≤ 24h; без валидной подписи → 400.
- **Development:** `Auth:AllowDevInitData=true` → bare Telegram id как initData (локальная отладка / integration tests).
- Ответ: `{ token: JWT(HS256, 24h), user }`. Канонический вызов — `Authorization: Bearer <JWT>`.
- Header `X-Telegram-User-Id` — **только Development**.
- Rate limit: 30 req/min per IP на `/api/auth/telegram`.

## Telegram Bot (Stage 3)

- `TelegramLongPollingService` (BackgroundService) + `TelegramBotApi` (HttpClient → Bot API).
- **Development mock:** `Telegram:UseMock=true` → `MockTelegramBotApi` (in-memory inbox/outbox); dev API `/api/dev/telegram/{message,callback,outbox}`; long polling работает без живого token.
- Без mock и без `Telegram:BotToken` polling **выключен** (no-op).
- Команды: `/start` `/new` `/create` — диалог создания; `/list`; `/preview <id>`; `/cancel`; `/help`.
- Диалог: заголовок → описание → (карточки через Mini App) → ссылка preview/publish.
- Publish: каждая карточка уходит **историей Telegram** (`postStory`, business connection, 24 ч);
  карточки **с фото** — исходные байты + caption ≤2048; карточки **только с текстом** —
  серверный рендер `StoryCardRenderer` (SkiaSharp): фон шаблона cover-кропом или белый,
  абзацы стилей (Inter/Playfair, S/M/L) по центру нижней трети кадра **1080×1920**,
  без caption (текст уже на изображении); **чат бота молчит** — успех/ошибка показываются
  окнами в Mini App; подключение приходит update'ом `business_connection` (Настройки → Бизнес).
- Авто-tenant + free subscription при первом `/start`.

## Mini App (Stage 3)

- Auth: `window.Telegram.WebApp.initData` → JWT; Dev login (prompt id) вне Telegram.
- Home: список публикаций + create; Preview: карусель cards; Publish: platform accounts + ready + publish.

## Instagram Adapter (Stage 4)

- `IInstagramPublisher`: `MockInstagramPublisher` (Development) или `InstagramGraphApiPublisher` (Meta Graph API).
- **Development mock:** `Instagram:UseMock=true` (default) → in-memory outbox; dev API `/api/dev/instagram/{outbox,fail-next}`; publish без живого токена.
- **Real Graph API:** `Instagram:UseMock=false` + `Instagram:AccessToken` + `Instagram:PublicMediaBaseUrl` (публичный URL для `GET /api/media/{id}` — Instagram не принимает binary upload).
- Flow: child containers (image_url) → CAROUSEL parent → `media_publish`.
- Требование: ≥1 карточка с `mediaAssetId`; 2+ изображений → CAROUSEL, 1 → single photo post.

## Carousel Templates (Stage 4)

- Каталог из **8 шаблонов** (`carousel_templates`): code, name, default styles (jsonb), background media.
- Фоны генерируются SkiaSharp при seed (`TemplateSeeder` + `BackgroundGenerator`, 1080×1080 PNG) и хранятся как system MediaAsset (tenant `00000000-...-0001`).
- `Publication.TemplateId` — apply на всю карусель (`PATCH /api/publications/{id}`).
- Mini App: выбор шаблона — единый блок «Фон карточки» в форме добавления карточки
  (шаблон ИЛИ файл, после выбора панель сворачивается в строку с «Изменить»);
  preview с `background-image` + текст поверх; CardEditor prefill styles из шаблона.
- **Вне scope:** server-side composition текста на фоне для Instagram publish image.

## Group chat Mini App (Stage 4)

- initData несёт `chat_id` / `chat_type` при открытии из group/supergroup/channel.
- `GET /api/telegram/chat-access?chatId=` → `getChatMember` → `{ isAdmin, canPublish }`.
- **Без админ-прав:** UI только предупреждение, без редактора/шаблонов.
- **С админ-правами:** обычная запись; publish → `POST /publications/{id}/publish/chat` (сервер повторно проверяет admin).
- Mock: `GetChatMember` всегда `administrator` (non-admin — только на живом боте).

## Валидация медиа (Stage 2)

Порядок проверок:

1. non-empty multipart
2. magic bytes → JPEG / PNG / WebP (MIME клиента не доверяем)
3. разбор заголовка файла → Width/Height (без полного decode)
4. `SizeBytes ≤ MaxImageSizeMb` и max сторона ≤ `MaxImageDimensionPx` (free fallback: 5 MB / 2048 px)
5. save + Width/Height
6. при attach: `MediaAsset.TenantId == Publication.TenantId` (иначе 403)

Реализация: `HeaderImageProbe` + `MediaStorage.BelongsToTenantAsync`.

## Подписки (Stage 2)

- `POST /api/tenants` авто-создаёт **free** subscription (30 дней).
- Lazy-bootstrap в `EfBillingQuery.GetActiveSubscriptionAsync`, если подписки нет.
- Без активной подписки publish → 403 `SubscriptionRequiredException`.
- Инкремент `usage_counters` — атомарный `INSERT … ON CONFLICT DO UPDATE`.
- Квота тратится только при ≥1 успешной delivery.

## Оплата публикации Telegram Stars (Stage 5)

- Гейт в `publish/me`: платим если **≥1 карточки с фото ИЛИ включён крыжек
  «Отключить рекламу бота»** (`noAds`), и username не в `Billing:FreeUsernames`
  → `200 {status:"PaymentRequired", invoiceUrl}` (Payment Pending,
  идемпотентный ключ `pub:{id}`, валюта XTR). **Оба условия вместе ≠ 2 звезды** —
  цена всегда `Billing:StarsPrice` (1), не суммируется.
- Крыжек «Отключить рекламу бота» (разовый): сохраняется в `publications.no_ads` →
  в историях нет ни водяной знака, ни link-зоны, на карточках Mini App нет плашки;
  выбор участвует в retry публикации.
- Оплата: `createInvoiceLink` → `WebApp.openInvoice` (Mini App) → update
  `message.successful_payment` (long polling) → `Payment.Completed` + `BillingEvent`.
  Фронт после `paid` ретраит publish до 6×1.2 с, пока сервер не подтвердит оплату.
- **Оплаченная публикация обходит квоту/подписку** (`skipQuota` в `PublishCoordinator`)
  и не расходует месячную квоту free-плана; `publish/retry` оплаченной публикации — тоже.
- Конфиг (fail-fast при старте, лог + throw): `Billing:StarsPrice` — целое ≥1;
  `Billing:FreeUsernames` — username без оплаты (`@` опционален, регистр не важен;
  dev: `devuser` — фикстурный username dev-логина по голому id, в
  appsettings.Development.Local.json; прод через env `Billing__FreeUsernames__0=@vasya`).
- Dev/тесты: `POST /api/dev/billing/stars-paid {publicationId}` (Development-only)
  имитирует успешную оплату.
- **Ссылка на бота**: в каждой истории — видимая надпись `t.me/<бот>` в правом нижнем углу
  (печать SkiaSharp, `StoryLinkStamp`) + кликабельная зона `StoryAreaTypeLink` поверх неё
  (из истории можно перейти). На карточках Mini App (редактор/превью) — плашка-ссылка.
  Username: приоритет `Telegram:BotUsername` (тестовый/прод бот) → `getMe` по токену
  среды (кэш) → `mockbot`; эндпоинт `GET /api/telegram/bot-username`.
- **Возврат при неудаче**: если публикация фейлится по «фундаментальному» коду
  (`NO_BUSINESS_CONNECTION`, `NO_CONTENT`, `BOT_NOT_CONFIGURED`, `BAD_CHANNEL`) —
  авто-`refundStarPayment`, ответ 409 с «Звёзды возвращены», `Payment → Refunded`.
  Транзиентные фейлы (`HTTP`, `INTERNAL`, …) оплату сохраняют — повтор бесплатен.
  После refund `publish/me` и `publish/retry` требуют **нового** инвойса
  (Payment сбрасывается в Pending, старая ссылка не переиспользуется);
  `payment.ExternalId` хранит `charge_id` после оплаты (нужен для refund).

## Администраторы и статистика

- **Список администраторов** `Admin:Usernames` — Telegram-алиасы (`@` опционален,
  регистр не важен; нормализация идентична `Billing:FreeUsernames`).
  Fail-fast при старте: пустые элементы и элементы с пробелами → `InvalidOperationException`.
  Dev: `devuser` в appsettings.Development.Local.json; прод через env
  `Admin__Usernames__0=@username`.
- `UserDto.IsAdmin` (auth + `/api/me`) — фронт по нему показывает ссылку «Статистика»
  в хедере и роут `/stats`.
- **`GET /api/admin/stats?days=30`** (days клампится 1..365, по умолчанию 30) —
  глобальная статистика по **всем тенантам**, только для админов
  (иначе 403 `ForbiddenException` → `ACCESS_DENIED` в логе + audit `ACCESS:API_DENIED`):
  - `totals` — публикаций за период, сумма и количество оплат Stars
    (только `Completed` в `XTR`; `Refunded` не считается поступлением);
  - `dayStats` — по дням (UTC): публикации, Stars, оплаты;
  - `byTemplate` — публикации по шаблонам (null → «без шаблона»);
  - `byCardCount` — распределение по количеству карточек (0..5, «6+»);
  - `counters` — `withBotAds` (noAds=false), `noAds` (крыжек включён),
    `withOwnImages` (хотя бы одна карточка со своим фото).
- UI: страница `/stats` (переключатель 7/30/90 дней, таблицы + карточки итогов).

## Статусы и publish (Stage 2)

`Draft → Ready → Publishing → Published | PartiallyFailed | Failed`

- `POST /publications/{id}/ready` — pre-publish валидация (≥1 карточка, text/media, media принадлежит tenant).
- Publish **из Draft** → 400 с hint «сначала ready».
- Publish разрешён из `Ready`, `PartiallyFailed`, `Failed` (retry).

## Логирование

- **App-лог** (`logs/carousel-api-*.log`): каждый запрос API — `{ClientIp} {UserName}`
  (middleware `RequestLogContextMiddleware` кладёт свойства в LogContext до ExceptionHandler/request
  logging; username резолвится из Bearer/dev-заголовка, `-` для анонимных). Шаблон:
  `… {RequestId} {ClientIp} {UserName} {Message}`.
- **Аудит** (`logs/audit-*.log` + таблица `audit_events`): только события `SourceContext='Audit'`
  (`{ActionCode} … login=… ip=… result=…`); служебный шум (EF/host/HTTP) отсечён фильтром
  `ByIncludingOnly` (пакет `Serilog.Expressions`) + Override на вложенном логгере.
  `UserIp` берётся из переданного `AuditEntry.UserIp` либо из текущего запроса (fallback в
  `AuditService`) — `unknown` только вне HTTP-контекста.
- **Клиентский IP за прокси**: контур клиент → Caddy (TLS) → nginx → api.
  Заголовки: **X-Forwarded-For** (основной), **Forwarded** (RFC 7239), **X-Real-IP** (nginx) —
  все включены в `ForwardedHeadersOptions`; разбор `Connection.RemoteIpAddress` после
  `UseForwardedHeaders`. nginx **не дописывает** свой `$remote_addr` в XFF (map `$client_ip`:
  проброс цепочки от Caddy, иначе TCP-пир) — иначе .NET (разбор справа налево) вернёт
  внутренний IP хопа. Спуфинг заголовков закрыт loopback-публикацией порта api; при открытии
  порта наружу вернуть `KnownIPNetworks` = docker-подсети.
- **HTML**: прод — nginx access-log в combined → `docker logs carousel-web` (IP/путь/UA;
  username для статики невозможен — nginx не знает JWT). Dev — Vite, без IP/username.
- Rate-limit отклонения и `ACCESS_DENIED` — с IP (как прежде).

## Очистка данных (retention)

- **Фоновый джоб** `DataCleanupHostedService` (BackgroundService): первый прогон через 10 сек
  в Development / 5 минут в проде, далее каждые `Cleanup:IntervalHours` (default 24).
- **Что удаляется:**
  - `publications` старше `Cleanup:PublicationRetentionDays` (default **180 дней**) — каскадом
    `cards`, `publication_targets`, `publication_deliveries`; исключается статус `Publishing` (в полёте);
  - `media_assets` — **осиротевшие** (нет ссылок из карточек и фонов шаблонов) старше
    `Cleanup:OrphanMediaRetentionDays` (default **30 дней**); bytea освобождается, подхватывает autovacuum.
- **Что НЕ трогается:** `payments`/`billing_events` (финансовая история; FK к публикации нет —
  `IdempotencyKey`), `audit_events` (журнал безопасности — правило AGENTS, хранится бессрочно).
- Логика вынесена в `IDataCleanupService.RunOnceAsync` (Scoped, тестируется напрямую); джоб — обёртка.
- Конфиг `Cleanup:*` (fail-fast: дни/интервал ≥1), env `Cleanup__Enabled=false` и т.д.
- Замечание: сервер дедуплицирует загрузки медиа по sha256 — осиротевание оценивается по FK.

## CORS / limits

- CORS: `Cors:AllowedOrigins` (Mini App `http://localhost:5173` по умолчанию).
- Kestrel `MaxRequestBodySize` = 20 MB.

## Что вне Stage 4

Полный OAuth Instagram (redirect + token exchange), покупка подписки через Stars/Web (checkout-флоу, webhook-обработчик — заглушки), фоновая очередь publish, S3, AI, шаблонный редактор, webhook-транспорт бота.
