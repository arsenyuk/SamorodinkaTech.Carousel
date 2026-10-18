# Модель данных

PostgreSQL, EF Core. Все PK — `Guid`, времена — UTC.

## Сущности

| Таблица | Назначение |
| --- | --- |
| `users` | Пользователь, `TelegramUserId` unique |
| `tenants` | Рабочее пространство заказчика |
| `tenant_members` | Роли Owner/Editor/Viewer |
| `publications` | Черновик/публикация |
| `cards` | Карточки с порядком и ссылкой на медиа |
| `media_assets` | Метаданные + `content bytea` |
| `platform_accounts` | Telegram-канал или Instagram-аккаунт |
| `platform_credentials` | Зашифрованные токены (`IDataProtector`) |
| `publication_targets` | Целевая площадка публикации |
| `publication_deliveries` | Попытки отправки, external id, ошибка |
| `plans`, `plan_limits` | Тарифы и ограничения |
| `subscriptions` | Подписка тенанта |
| `payments`, `billing_events` | Платежи и события (идемпотентность через `idempotency_key`) |
| `usage_counters` | Учёт лимитов по месяцам |
| `bot_conversation_states` | Шаг диалога бота |
| `audit_events` | Журнал |

## Каскады

- `tenants` → `publications` (Cascade)
- `publications` → `cards`, `publication_targets` (Cascade)
- FK к `users` — Restrict (не удаляем пользователя «вместе с» тенантом каскадом извне)

## Сиды

`DbSeeder` создаёт тарифы `free` (3 публикации/мес, 5 карточек) и `pro`.
