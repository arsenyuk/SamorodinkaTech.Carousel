# API-контракты (Stage 3)

Базовый URL: `/api`.

## Auth

| Метод | Путь | Описание |
| --- | --- | --- |
| POST | `/auth/telegram` | `{ initData }` → `{ token: JWT, user }` |
| GET | `/me` | Текущий пользователь |

**initData**

- Production: Telegram Mini App initData с HMAC (bot token); иначе 400.
- Development (`Auth:AllowDevInitData=true`): bare Telegram id.

**Auth headers**

- `Authorization: Bearer <JWT>` (HS256, 24h).
- `X-Telegram-User-Id` — **только Development**.
- `X-Tenant-Id` — обязателен для workspace-скоупа; иначе 400. Невидимый tenant → 403.

**Rate limit:** `/api/auth/telegram` — 30 req/min per IP; `/api/media` — 20 req/min.

## Tenants

| Метод | Путь | Описание |
| --- | --- | --- |
| GET | `/tenants` | Список workspace |
| POST | `/tenants` | `{ name }` → создаёт Owner-membership + free subscription |

### Members (Owner-only для write)

| Метод | Путь | Описание |
| --- | --- | --- |
| GET | `/tenants/{id}/members` | Viewer+ |
| POST | `/tenants/{id}/members` | `{ telegramUserId, role: "Editor"\|"Viewer" }` |
| PATCH | `/tenants/{id}/members/{memberId}` | `{ role }` |
| DELETE | `/tenants/{id}/members/{memberId}` | Удалить участника |

## Publications

| Метод | Путь | Роль | Описание |
| --- | --- | --- | --- |
| GET | `/publications` | Viewer | Список (фильтр `?tenantId=`) |
| POST | `/publications` | Editor | `{ title, description? }` → Draft |
| GET | `/publications/{id}` | Viewer | Детали + cards + targets |
| PATCH | `/publications/{id}` | Editor | `{ title?, description? }` |
| DELETE | `/publications/{id}` | Editor | Удалить |
| POST | `/publications/{id}/ready` | Editor | Draft → Ready (валидация контента) |
| POST | `/publications/{id}/cards` | Editor | `{ text?, mediaAssetId?, styles? }` |
| PATCH | `/publications/{id}/cards/{cardId}` | Editor | `{ text?, mediaAssetId?, order?, styles? }` |
| DELETE | `/publications/{id}/cards/{cardId}` | Editor | Удалить карточку |
| DELETE | `/publications/{id}/cards` | Editor | Очистить все карточки → Draft |
| POST | `/publications/{id}/cards/reorder` | Editor | `{ cardIds: guid[] }` |
| POST | `/publications/{id}/publish` | Editor | `{ accountIds }`; из Draft → 400 |
| POST | `/publications/{id}/publish/me` | Editor | Карусель в Telegram-чат текущего пользователя (DM). Без ручного подключения. Из Draft → 400 |
| POST | `/publications/{id}/publish/chat` | Editor | `{ chatId }` — publish в группу/канал. Сервер проверяет `getChatMember` (admin/creator); иначе 403 |
| POST | `/publications/{id}/publish/retry` | Editor | Только failed/pending targets |

`styles` — необязательный массив `{ fontName, color, fontSize, fontStyle }` (валидация `CardStyleRules`).

## Media

| Метод | Путь | Роль | Описание |
| --- | --- | --- | --- |
| POST | `/media` | Editor | `multipart/form-data` field `file`; JPEG/PNG/WebP magic-bytes |
| GET | `/media/{id}` | Viewer | Отдать изображение (tenant-scoped) |

Ответ `POST /media`: `{ id, contentType, sizeBytes, width, height }`.

Отклонения:

- non-image / corrupt → 400
- size / dimension over plan → 403 `LimitExceeded`
- cross-tenant attach на карточке → 403

## Telegram chat access (group Mini App)

| Метод | Путь | Описание |
| --- | --- | --- |
| GET | `/api/telegram/chat-access?chatId=` | `{ chatId, isMember, isAdmin, status, canPublish }` — проверка прав в чате через Bot API `getChatMember` |

Mini App из группового чата: initData несёт `chat_id` / `chat_type`. UI без админ-прав показывает только предупреждение. Publish в чат — `POST /publications/{id}/publish/chat`.

## Templates (Stage 4)

Глобальный каталог 8 предустановленных шаблонов (фон + дефолтные стили). Шаблон применяется ко всей публикации.

| Метод | Путь | Описание |
| --- | --- | --- |
| GET | `/templates` | Список: `{ id, code, name, description, backgroundMediaAssetId, defaultStyles }` |
| GET | `/templates/{id}/background` | PNG-фон шаблона |

**Apply template:** `PATCH /publications/{id}` body `{ templateId }` — привязывает шаблон (Editor+). Не перезаписывает текст карточек.

Коды: `aurora`, `noir`, `sand`, `ocean`, `bloom`, `forest`, `mono`, `sunset`.

## Platform accounts

| Метод | Путь | Роль |
| --- | --- | --- |
| GET | `/platform-accounts` | Viewer |
| POST | `/platform-accounts` | Owner |
| DELETE | `/platform-accounts/{id}` | Owner |

`POST /platform-accounts`: `{ platform: "Telegram"|"Instagram", externalId, displayName }`. Для Instagram внешний id = ig-user-id.

## Dev Instagram mock (Development only)

Требует `Instagram:UseMock=true` (`InstagramMockOutbox` в DI).

| Метод | Путь | Описание |
| --- | --- | --- |
| GET | `/api/dev/instagram/outbox` | Опубликованные mock-карусели |
| DELETE | `/api/dev/instagram/outbox` | Очистить outbox |
| POST | `/api/dev/instagram/fail-next` | Следующий publish вернёт failure (retry-тесты) |

## Billing

| Метод | Путь | Роль | Описание |
| --- | --- | --- | --- |
| GET | `/billing/plans` | — | Активные тарифы + limits |
| GET | `/billing/subscription` | Viewer | Текущая подписка (auto free) |
| POST | `/billing/checkout` | Owner | `{ planId, provider }` |

## Webhooks (заглушки)

| Метод | Путь |
| --- | --- |
| POST | `/webhooks/telegram-stars` |
| POST | `/webhooks/payments/{provider}` |
| POST | `/webhooks/telegram/bot` |

## Health

| Метод | Путь |
| --- | --- |
| GET | `/health` |
| GET | `/health/ready` |

Swagger: `http://localhost:5050/swagger` в Development.

## HTTP-коды исключений

| Исключение | Код |
| --- | --- |
| `UnauthorizedException` | 401 |
| `ForbiddenException` / `LimitExceededException` / `SubscriptionRequiredException` | 403 |
| `ValidationException` / FluentValidation | 400 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
