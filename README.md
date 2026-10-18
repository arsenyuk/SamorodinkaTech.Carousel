# SamorodinkaTech.Carousel

SaaS создания и публикации каруселей: Telegram Mini App + Instagram (Business/Creator). Модульный монолит на ASP.NET Core.

**Stage 4 (текущий):** Telegram Bot (long polling), Mini App login/list/publish, HMAC initData + JWT, rate limits, Instagram Adapter (mock + Graph API), 8 carousel templates (фон + стили).

См. [docs/architecture.md](docs/architecture.md), [docs/data-model.md](docs/data-model.md), [docs/api-contracts.md](docs/api-contracts.md), [docs/security-audit.md](docs/security-audit.md), [docs/telegram-bot-setup.md](docs/telegram-bot-setup.md) — регистрация бота и токен.

## Стек

- .NET 10 / ASP.NET Core Minimal API
- EF Core 10 + PostgreSQL 16
- SkiaSharp 4.x (генерация фонов шаблонов; проба изображений — по заголовкам без decode)
- FluentValidation, Serilog, HealthChecks
- React 18 + Vite + TypeScript (Mini App)
- xUnit + Testcontainers

> Примечание: в исходном плане заявлен .NET 8 LTS; в репозитории используется net10.0 (миграция 2026-10-19).

## Структура

```
src/Carousel.Domain            сущности и правила
src/Carousel.Application       DTO, валидаторы, интерфейсы, MediaValidation
src/Carousel.Infrastructure    PostgreSQL, SkiaSharp, stub-адаптеры
src/Carousel.Api               Endpoints/*.cs
src/frontends/mini-app         Telegram Mini App (preview)
tests/                         unit + integration
docs/                          архитектура, модель, API
```

## Запуск

```bash
# 1. PostgreSQL
docker compose up -d

# 2. API (Development: включён in-process mock Telegram-бота)
dotnet run --project src/Carousel.Api

# 3. Tests
dotnet test Carousel.slnx

# 4. Mini App
cd src/frontends/mini-app
cp .env.example .env
npm install
npm run dev
```

Swagger: `http://localhost:5050/swagger` (Development).

### Локальный mock Telegram (Development)

В Development `Telegram:UseMock=true` — long polling работает **внутри API** без живого bot token.

В Development `Instagram:UseMock=true` — publish в Instagram идёт в in-memory outbox:

```bash
curl -s http://localhost:5050/api/dev/instagram/outbox
curl -s -X POST http://localhost:5050/api/dev/instagram/fail-next   # следующий publish упадёт
```

Диалог с ботом через curl (в примерах `111111111` — просто любое dev-число, подставьте своё):

```bash
# «Пользователь» пишет боту /start
curl -s -X POST http://localhost:5050/api/dev/telegram/message \
  -H 'Content-Type: application/json' \
  -d '{"fromUserId":111111111,"text":"/start","fromUsername":"devuser","fromFirstName":"Dev"}'

# через ~1s ответ бота
curl -s http://localhost:5050/api/dev/telegram/outbox

# заголовок → описание → «пропустить» → ссылка Mini App в outbox
curl -s -X POST http://localhost:5050/api/dev/telegram/message \
  -H 'Content-Type: application/json' \
  -d '{"fromUserId":111111111,"text":"Моя первая карусель"}'

curl -s -X POST http://localhost:5050/api/dev/telegram/message \
  -H 'Content-Type: application/json' \
  -d '{"fromUserId":111111111,"text":"пропустить"}'

# очистить outbox
curl -s -X DELETE http://localhost:5050/api/dev/telegram/outbox
```

Mini App auth: вход только через initData (внутри Telegram — автоматически; если
войти не удалось, Mini App **закрывает себя**, кнопок логина в UI нет).

Локальные настройки разработчика — в `src/Carousel.Api/appsettings.Development.Local.json`
(в `.gitignore`, в git не попадает). Для публикаций без оплаты Stars создайте файл:

```json
{
  "Billing": { "FreeUsernames": [ "devuser" ] }
}
```

`devuser` — фикстурный username dev-логина по голому числовому id в `?tgInitData=`
(любое число); в проде в exempt пишутся реальные `@username`.

Локально без Telegram — через query-параметр или скрипт:

```bash
# откройте с любым числовым id (любое число → username devuser, exempt от оплаты):
# http://localhost:5173/?tgInitData=111111111

# или скрипт (ждёт API+Vite, затем `open` в браузере):
./scripts/open-miniapp.sh --wait
./scripts/open-miniapp.sh --wait --tg-id 999
```

Реальный bot: задайте `Telegram__BotToken` и `Telegram__UseMock=false`.

### Smoke

```bash
# login (dev: raw telegram id as initData — подойдёт любое число)
curl -s -X POST http://localhost:5050/api/auth/telegram \
  -H 'Content-Type: application/json' \
  -d '{"initData":"111111111"}'

# then use Bearer token + X-Tenant-Id as returned/created
```

## Roadmap (по плану проекта)

1. ✅ Этап 1 — скелет и спецификация
2. ✅ Этап 2 — Backend: tenant isolation, RBAC, media validation, publish hardening
3. ✅ Этап 3 — Telegram Bot + Mini App preview/publish + JWT/HMAC
4. ✅ Этап 4 — Instagram Adapter (mock + Graph API publish; OAuth — отдельно) + 8 шаблонов карусели
5. Этап 5 — Биллинг (Stars + web), лимиты, возвраты
6. Этап 6 — Защита, эксплуатация, пилот
