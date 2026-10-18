# Отчёт об уязвимостях — SamorodinkaTech.Carousel (Stage 1+2)

**Дата аудита:** 2026-10-18  
**Последнее обновление:** 2026-11-19 (в L11 внесены **номера критических CVE** — CVE-2026-111497, CVE-2026-31789; повторный скан на обновлённой базе CVE: в L12 обнаружен **CVE-2025-68121** (CRITICAL) и HIGH вырос 31→56, L12 переведён Low→Medium; проверена стабильная nginx 1.30.7); 2026-11-18: сканирование контейнеров Trivy (L11–L13), L9–L10 контейнер, L3 SkiaSharp  
**Scope:** Stage 1+2 backend — auth, RBAC, multi-tenancy, media, billing, publish, dependencies, config  
**Метод:** Manual code review (read-only) + dependency posture (NuGet / NU1903)  
**Стек:** .NET 10 / ASP.NET Core Minimal API, EF Core 10, PostgreSQL 16, SkiaSharp 4.156.1  

---

## Executive summary

Аудит выявил **3 Critical**, **6 High**, **6 Medium**, **8 Low** и ряд Info-замечаний. Два независимых Critical-дефекта **полностью обесценивают multi-tenancy и RBAC** до тех пор, пока они не исправлены:

1. **Bearer-токен = первичный ключ пользователя (Guid).** Токен не подписан, не истекает, не отзывается; Guid раскрывается любому участнику tenant'а через `GET /tenants` и `GET /tenants/{id}/members`. Владение Guid = полная имперсонация (в т.ч. Owner).
2. **Stub Telegram-аутентификации не проверяет initData.** Любой Telegram user id (или произвольная строка) принимается как валидная сессия **во всех окружениях** (DI-registration без gate на Development).

Вместе они дают one-request эскалацию: Viewer → `GET members` → `POST /auth/telegram` с Owner TelegramUserId → полные Owner-привилегии.

**Блокеры до любого non-dev деплоя:** C1 (токен=PK), C2 (stub initData).  
До пилота также: rate limiting, media bomb, quota TOCTOU, retry-bypass.

| Severity | Кол-во (открытых) |
|----------|-------------------|
| Critical | 0 (C1 ✅, C2 ✅ — закрыты Stage 3) |
| High | 0 (H1 ✅, H5 ✅, H6 ✅) |
| Medium | 2 (L11 — nginx: 3 CRITICAL-строки; L12 — postgres: 1 CRITICAL в gosu/Go stdlib) |
| Low | 3 (L9, L10, L13; L3 ✅ закрыт) |

> **Закрыто:** C1 ✅, C2 ✅, C3 ✅, H1 ✅, H2 ✅, H3 ✅, H4 ✅, H5 ✅, H6 ✅, M1 ✅, M2 ✅, M3 ✅, M4 ✅, M5 ✅, M6 ✅, L1 ✅, L2 ✅, L3 ✅, L4 ✅, L5 ✅, L6 ✅, L7 ✅, L8 ✅ — см. Changelog.

---

## Методология

- Manual review всех endpoint'ов `src/Carousel.Api/**`, `src/Carousel.Application/**`, `src/Carousel.Infrastructure/**`
- Проверка DI-регистраций, exception-handler, CORS, request limits, docker-compose, Directory.Packages.props
- Dependency posture: NuGet versions, отсутствие `NoWarn`/audit suppressions; исторические ImageSharp CVE (3.1.x) — пакет поднят до 4.1.3
- **2026-11-18:** добавлено сканирование контейнеров **Trivy 0.76.2** (OS/пакеты/base-образы) и `npm audit` — см. раздел «Сканирование уязвимостей контейнеров»
- **Не делалось:** dynamic pentest, black-box, SAST-сканер, fuzzing

Файлы-источники: `Program.cs`, `Endpoints/*.cs`, `Auth/*`, `Publishing/*`, `Billing/*`, `Images/*`, `Storage/*`, `Validators/*`, `Directory.Packages.props`, `docker-compose.yml`, `appsettings*.json`.

---

## Находки

### Critical

#### C1. Bearer-токен = user PK Guid; Guid раскрывается участникам → имперсонация Owner

| | |
|--|--|
| **Severity** | Critical |
| **Location** | `src/Carousel.Api/Endpoints/AuthEndpoints.cs:27-30`, `src/Carousel.Infrastructure/Auth/CurrentUserContext.cs:47-58`, `src/Carousel.Api/Endpoints/TenantEndpoints.cs:31,86`, `src/Carousel.Application/DTOs/Contracts.cs:9,58` |

**Description.** Токен — это `user.Id.ToString("N")`. `CurrentUserContext` принимает любой well-formed Guid пользователя как валидный Bearer. Нет подписи, expiry, revocation. Параллельно user Guid возвращается в обычных ответах: `TenantDto.OwnerUserId` и `TenantMemberDto.UserId` (доступно Viewer'у).

**Exploit.** Атакующий-Viewer вызывает `GET /api/tenants` или `GET /api/tenants/{id}/members`, читает Owner Guid и шлёт `Authorization: Bearer <ownerGuid>` — полные Owner-права (members, platform accounts, billing checkout). Утечка Guid в логах/proxy = permanent unrevocable credential.

**Fix.** Signed expiring tokens (JWT / server-side session с hashed lookup); не использовать PK как credential; не отдавать raw user Guids в member/tenant DTO'ах; добавить logout/revocation.

---

#### C2. Stub Telegram auth: любой Telegram id = валидная сессия (все окружения)

| | |
|--|--|
| **Severity** | Critical |
| **Location** | `src/Carousel.Infrastructure/Auth/StubTelegramAuthService.cs:23-57`, `InfrastructureServiceCollectionExtensions.cs:28` |

**Description.** `AuthenticateAsync` не делает HMAC-проверки. Три unauthenticated-пути:
1. query-string initData парсится и доверяется как есть;
2. bare `long` initData = Telegram user id («local debug», без gate);
3. любая строка → детерминированный fake id через SHA256 + auto-provision user row.

Stub зарегистрирован безусловно — нет `IsDevelopment`-гейта на DI.

**Exploit.** `POST /api/auth/telegram` с `{"initData":"<victimTelegramId>"}` → Bearer victim. Путь 3 даёт minting unlimited user accounts.

**Fix.** Реальная валидация Mini App initData: `HMAC-SHA256(data_check_string, bot_token)` + `auth_date` freshness. Raw-id/SHA256 fallback — **только Development** (gate при регистрации или throw).

---

### High

#### H1. Цепочка Viewer→Owner: members listing + stub auth

| | |
|--|--|
| **Severity** | High |
| **Location** | `TenantEndpoints.cs:83-89` + `StubTelegramAuthService.cs:49-52` |

`GET /tenants/{id}/members` (Viewer+) возвращает `TelegramUserId` каждого участника. В связке с C2 Telegram id = полный credential.

**Exploit.** Viewer → `GET members` → строка `Role=="Owner"` → `POST /auth/telegram {"initData":"<ownerTgId>"}` → Bearer Owner'а.

**Fix.** В первую очередь C2. Дополнительно: ограничить `TelegramUserId` в DTO (только Owner) или invite-code flow для add member.

---

#### H5. Нет rate limiting на API

| | |
|--|--|
| **Severity** | High |
| **Location** | `Program.cs:21-41` (нет `AddRateLimiter` / `UseRateLimiter` нигде в `src`) |

Ничего не throttle'ит `POST /api/auth/telegram` (создаёт DB rows через stub), `POST /api/tenants` (tenant + free sub), media uploads.

**Exploit.** Script loop auth → unbounded User rows; tenant/subscription spam; memory pressure через mass upload (в связке с H2/L5).

**Fix.** `AddRateLimiter`: strict per-IP на `/api/auth/*`, `/api/tenants` (POST), `/api/media`; global fallback.

---

### Low

| ID | Заголовок | Location | Кратко | Fix |
|----|-----------|----------|--------|-----|
| ~~L2~~ | 403 vs 404 existence oracle | `EndpointHelpers.cs`, `PlatformAccountEndpoints.cs`, `MediaEndpoints.cs` | **Закрыт:** фильтрация по visible tenants → cross-tenant / not-found = 404 | ✅ Done |
| ~~L3~~ | ImageSharp 4.x Split-License | `Directory.Packages.props` | **Закрыт 2026-11-17:** миграция на SkiaSharp (Apache-2.0); SixLabors.* полностью удалены; `dotnet publish -c Release` собирается | ✅ Done |
| L9 | Контейнер запускается от root | `Dockerfile` (stage `api`) | `docker exec carousel-api id` → `uid=0(root)`; повышенная цена обхода при компрометации контейнера | `USER app` в финальном stage (пользователь есть в aspnet-образе) + права на `/app/logs`, `./keys` |
| L10 | Dev-конфиг в слое образа | `dotnet publish` копирует `appsettings*.json` | `appsettings.Development.Local.json` (dev-настройки) попадает в образ; в Production **не применяется** (среда не совпадает), но это лишние dev-данные в прод-артефакте | Исключить файл в `.dockerignore`: `src/Carousel.Api/appsettings.Development.Local.json` |

---

### Info / positive controls (проверено — не уязвимости)

| # | Тема | Результат |
|---|------|-----------|
| I1 | SQL injection | **Нет.** Единственный raw SQL (`EfBillingQuery.IncrementMonthlyPublicationsAsync`) — EF параметры `{0},{1},{2}`; остальное LINQ |
| I2 | Path traversal / storage | **Нет surface.** `StorageKey = pg://{tenant}/{guid}`; bytes в Postgres bytea; multipart filename не используется |
| I3 | Content-Type spoofing | **Mitigated.** Тип из magic bytes, не из client MIME; GIF rejected |
| I4 | Tenant isolation (list endpoints) | **OK.** publications/media/platform-accounts/tenants фильтруются по memberships |
| I5 | Cross-tenant media attach | **OK.** Card create/update/ready/publish-check + integration test |
| I6 | Cross-tenant publish targets | **OK.** `a.TenantId == publication.TenantId` filter |
| I7 | Owner protection | **OK.** Нельзя назначить/снять Owner через member API; demote/remove Owner → 403 |
| I8 | Checkout Owner-gated | **OK.** |
| I9 | Dev header gate | **OK** (`IsDevelopment`), см. L1 про сообщение 401 |
| I10 | 500 responses | **OK.** Generic message; details server-side |
| I11 | Swagger | **OK.** Только Development |
| I12 | CORS | Acceptable; `AllowCredentials` не задан; origins из config |
| I13 | FluentValidation errors | Structured, не raw dumps |
| I14 | Dependencies | EF/Npgsql 9.0.4, Serilog 9.0.0, Swashbuckle 7.2.0 — current; ImageSharp 4.1.3 (3.1.x CVE закрыты апгрейдом); нет audit suppressions |

---

## Анализ уязвимостей библиотек (dependency audit)

**Дата проверки:** 2026-10-19 (после миграции на .NET 10 / EF Core 10)  
**Инструменты:** `dotnet list package --vulnerable`, `dotnet list package --outdated`

### Известные уязвимости (NuGet audit)

**Статус: 0 уязвимых пакетов во всех проектах** (production + tests).

Ранее обнаруженный transitive SSH.NET CVE (2× High через Testcontainers.PostgreSql 4.3.0) закрыт обновлением до Testcontainers 4.16.0.

### Текущие версии (после миграции 2026-10-19)

| Категория | Пакеты | Версия |
|-----------|--------|--------|
| **Runtime** | Target Framework | net10.0 |
| **EF Core** | EntityFrameworkCore, Relational, Design | 10.0.13 |
| **Npgsql** | Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| **ASP.NET** | OpenApi, DataProtection, JwtBearer, Mvc.Testing | 10.0.13 |
| **Extensions** | DependencyInjection.Abstractions | 10.0.13 |
| **Logging** | Serilog.AspNetCore | 10.0.0 |
| **Swagger** | Swashbuckle.AspNetCore | 10.3.0 |
| **Identity** | Microsoft.IdentityModel.JsonWebTokens | 8.23.0 |
| **Validation** | FluentValidation (+AspNetCore, DI) | 11.11.0 / 11.3.1 |
| **Health** | AspNetCore.HealthChecks.NpgSql | 9.0.0 (latest) |
| **Media** | SixLabors.ImageSharp | 4.1.3 |
| **Tests** | Testcontainers.PostgreSql | 4.16.0 |
| **Tests** | xunit.runner.visualstudio | 4.0.1 |
| **Tests** | Microsoft.NET.Test.Sdk | 18.11.0 |

### Осознанно отложенные обновления

| Пакет | Текущая | Latest | Причина |
|-------|---------|--------|---------|
| FluentValidation | 11.11.0 | 12.2.0 | Major; 11.x — стабильная линия, миграция отдельно |
| FluentAssertions | 7.2.0 | 8.11.0 | 8.x — commercial license; 7.x — latest free |
| Moq | 4.20.72 | 4.21.0 | Minor; не срочно |
| Serilog.Sinks.File | 7.0.0 | — | Актуальная |

### Политика обновления

1. **Security-patch:** немедленно при обнаружении NU1901–NU1904.
2. **Minor/patch:** в плановом окне (ежемесячно).
3. **Major (EF, FluentValidation):** только после согласования и полного тестового прогона.
4. **Запрещено:** `NoWarn` для NU1901–NU1904; подавление audit'а без явного согласования.

---

## Проблема ImageSharp (L3) — варианты устранения

**Статус: ЗАКРЫТ 2026-11-17 — реализован вариант 2 (миграция на SkiaSharp).** Ниже — анализ, сохранённый на момент решения.

**Дата анализа:** 2026-11-17. **Влияние (до закрытия):** Release-сборка (`dotnet publish -c Release`) падала с ошибкой лицензии Six Labors → деплой на прод был заблокирован.

### Где ImageSharp фактически используется

| Место | Назначение | Нужен в рантайме прода? |
|-------|-----------|------------------------|
| `Templates/BackgroundGenerator.cs` | Генерация PNG 1080×1080: вертикальный градиент + радиальный хайлайт + диагональные полосы (попиксельный математический код, **без рисования фигур и текста**) | **Да** — `TemplateSeeder` (стартовая заливка 8 фонов) и `TemplateBackgroundRenderer` (произвольные цвета в `TemplateEndpoints`) |
| `Images/HeaderImageProbe.cs` | Проба размеров по заголовку файла | Прода не касается — написан **без** ImageSharp (закрытие H2) |
| `Images/ImageSharpImageProbe.cs` | Тот же проб, полный decode | Только DI-fallback/отладка; в проде зарегистрирован `HeaderImageProbe` |
| `tests/.../ImageProbeTests.cs` | Генерация тестовых изображений | Только тесты |

**Ключевой факт:** пакеты `SixLabors.Fonts` и `SixLabors.ImageSharp.Drawing` **не используются ни в одном строке C#** (нет usings, нет вызовов). Ошибку лицензии поднимает именно их build-targets (`SixLabors.ImageSharp.Drawing.targets`). Ядро `SixLabors.ImageSharp` (4.1.3) само по себе лицензию на build не проверяет, но под Split License тоже требует плату для закрытого commercial SaaS — убрать только Drawing = убрать симптом (build-gate), но не нарушение лицензии.

### Варианты

| # | Вариант | Плюсы | Минусы | Оценка |
|---|---------|-------|--------|--------|
| **1** | **Купить коммерческую лицензию Six Labors** (`SixLaborsLicenseKey`/`SixLaborsLicenseFile` в билде) | Ноль кода; всё остаётся как есть; официально | Платёж (см. sixlabors.com/pricing); зависимость от вендора | Быстрый фикс, годится если покупка уже согласована |
| **2** | **Миграция на SkiaSharp** (Apache-2.0) | Бесплатно и коммерчески без ограничений; зрелая библиотека; перенос прямой: `SKBitmap` + переписанная попиксельная математика + `SKImage.Encode(Png)` | +1 нативная зависимость (native libs); правка 2-3 файлов + тестов | **Рекомендуемый** — соответствует политике «бесплатные либо купленные» зависимости |
| **3** | **Свой PNG-энкодер без зависимостей** (.NET `ZLibStream` + PNG-чанки, ~80 строк) | Нулевые зависимости; попиксельный код `BackgroundGenerator` сохраняется почти как есть (нужен свой `Color` вместо ImageSharp `Color`) | Свой код кодирования = своя ответственность (корректность/декодеры); нужен golden-тест против эталонного декодера | Минимальный surface, но рукописный бинарный формат |
| 4 | Убрать рантайм-генерацию: положить 8 фонов статическими ассетами | Исключает генерацию из прода | **Не закрывает кейс**: `TemplateEndpoints` генерирует фоны с произвольными hex-цветами пользователя → рантайм всё равно нужен | Неполный, только как дополнение |
| ~~5~~ | Снять/подавить build-gate Drawing без лицензии | — | Обход лицензионной проверки = нарушение лицензии | **Запрещено** |

### Рекомендация

**Вариант 2 (SkiaSharp)** как основной: убрать `SixLabors.ImageSharp`, `SixLabors.ImageSharp.Drawing`, `SixLabors.Fonts` из `Directory.Packages.props`/`Carousel.Infrastructure.csproj`; переписать `BackgroundGenerator` + `TemplateBackgroundRenderer` (там только `Color.ParseHex` → собственный парсер) и `ImageProbeTests`-фикстуры; `ImageSharpImageProbe` удалить (в DI и так `HeaderImageProbe`). Проба изображений и медиа-валидация не затрагиваются.

Срок: закрыть **до первого Release-деплоя** (блокер).

### Что проверить после устранения

```bash
dotnet publish src/Carousel.Api -c Release -o /tmp/carousel-release   # должен собраться
dotnet test Carousel.slnx                                              # 74/74 green
# + визуально: 8 шаблонных фонов и кастомный генератор в Mini App отдают валидный PNG
```

---

## Контейнерное развёртывание (Docker) — обследование

**Дата:** 2026-11-17. **Состав:** `Dockerfile` (multi-stage: sdk:10.0 → node:20 →
aspnet:10.0 + fontconfig → nginx:1.27-alpine), `docker/nginx.conf`, compose-профиль
`full` (api + web + postgres), `.dockerignore`, инструкция `docs/deployment.md`.

### Контроли (проверено)

- Секреты **не попадают в образ и контекст сборки**: `.env` исключён в `.dockerignore`,
  в git не трекается; в compose — только переменные окружения.
- Порты `api`/`web`/`postgres` привязаны к `127.0.0.1` — наружу только через
  reverse-proxy с TLS (Caddy по deployment.md).
- `postgres`: healthcheck + named volume; `depends_on: condition: service_healthy`.
- `./logs` и `./keys` (DataProtection) — volume на хосте, переживают пересоздание;
  ротация логов Serilog: 14 файлов / 100 МБ (см. L-находки по логам ранее).
- `restart: unless-stopped`; миграции — fail-fast при невалидном конфиге Billing.
- Startup-валидация конфига (цена Stars, exempt-список) — лог + throw до миграций.
- Образы: официальные `mcr.microsoft.com` / `mirror.gcr.io` (docker.io недоступен
  из сети сборки — зафиксировано в deployment.md).

### Находки

- **L9 — контейнер от root**: `docker exec carousel-api id` → `uid=0(root)`.
  Фикс: `USER app` в финальном stage + убедиться, что `./keys`/`/app/logs` доступны на запись.
- **L10 — dev-конфиг в образе**: `appsettings.Development.Local.json` копируется
  `dotnet publish` в артефакт (проверено на свежей публикации). В Production не
  активируется (среда не совпадает), но dev-данные в прод-слое — лишние. Фикс:
  строка в `.dockerignore`.
- **Info (принято, не находимость):** секреты в compose-env видны в
  `docker inspect` — для пилота приемлемо; при росте — Docker secrets/воркер-подход.
  TLS terminated внешним Caddy (внутренняя сеть compose — HTTP).

---

## Сканирование уязвимостей контейнеров (Trivy)

**Дата:** 2026-11-18; **повторный скан:** 2026-11-19 (база CVE пересобрана 07:10 —
в L12 появился CRITICAL). **Инструмент:** Trivy 0.76.2 (`--severity HIGH,CRITICAL
--scanners vuln --ignore-unfixed`).

| Образ | OS / состав | HIGH | CRITICAL | Статус |
|---|---|---|---|---|
| `samorodinkatechcarousel-api` | Ubuntu 24.04 (106 pkgs) + .NET 10.0.15 | **0** | **0** | ✅ чисто |
| `samorodinkatechcarousel-web` | nginx:1.27-alpine → **alpine 3.21.3** | **67** | **3** | ⚠️ L11; свежая стабильная `nginx:1.30-alpine` (1.30.7, alpine 3.24.4) просканирована 2026-11-19 → **0 CRITICAL / 0 HIGH** |
| `postgres:16-alpine` | alpine 3.24.1 (musl, openssl, krb5, zlib, libxslt…) + gosu (Go 1.24.6) | **56** | **1** (CVE-2025-68121) | ⚠️ L12 (обновлено 2026-11-19 на свежей базе CVE) |

**NuGet:** `dotnet list package --vulnerable` — **0 уязвимых** во всех проектах.
**npm (production):** `npm audit --omit=dev` — **2 moderate** в `react-router-dom@6` → L13.

### Критические CVE (L11, образ web)

Номера извлечены из отчёта Trivy (`trivy image --severity CRITICAL --scanners vuln
--ignore-unfixed --format json samorodinkatechcarousel-web`, 2026-11-19).
«3 CRITICAL» = 3 пакетные строки = **2 уникальных CVE** (openssl бьёт по libcrypto3
и libssl3 одним номером):

| CVE | CVSS 3.0 | Пакет(ы) | Установлено | Фикс | Описание |
|---|---|---|---|---|---|
| **CVE-2026-111497** | 9.8 | brotli-libs | 1.1.0-r2 | 1.1.1-r0 | Out-of-Bounds Write в декодере Google Brotli |
| **CVE-2026-31789** | 9.8 | libcrypto3, libssl3 (openssl) | 3.3.3-r0 | 3.3.7-r0 | Heap buffer overflow при разборе X.509-сертификата (32-bit) |

Обе закрываются обновлением base-образа (`apk upgrade` внутри свежего тега nginx —
фигурируют в alpine-репозитории апстрима). Выгрузка всех уровней (HIGH — 67 строк):
`trivy image --severity HIGH,CRITICAL --format table samorodinkatechcarousel-web`.

### Находки

| ID | Severity | Находка | Фикс |
|----|----------|---------|------|
| **L11** | Medium | База `nginx:1.27-alpine` устарела (alpine 3.21.3): 3 CRITICAL-строки = **CVE-2026-111497** (brotli OOB write, 9.8) + **CVE-2026-31789** (openssl heap overflow, 9.8) + 67 HIGH (c-ares и др.) — все **с фиксами**; номера см. в разделе «Критические CVE» | Перетянуть свежий тег (`mirror.gcr.io/library/nginx:…`) или перейти на `nginx:stable-alpine`, пересобрать web-образ |
| **L12** | Medium | Пакеты в `postgres:16-alpine` (база CVE 2026-11-19): **1 CRITICAL — CVE-2025-68121** (Go stdlib `crypto/tls`: неверная валидация сертификата, в бинарнике gosu 1.24.6 → фикс 1.24.13+) + **56 HIGH** (musl/openssl/krb5/zlib/libxslt…), все **с фиксами** | `docker pull postgres:16-alpine` (апстрим пересобирает тег) на машине сборки и на сервере, затем повторный Trivy-скан |
| **L13** | Low | `react-router-dom@6.28` → 2 moderate (open redirect в Link/useNavigate; конструктор-инъекция в SSR `deserializeErrors`) | Фикс только через major: `react-router-dom@7.18.5` (breaking — отдельная задача; SSR-вектор к нашему SPA неприменим, open redirect практический риск низкий — редиректов по вводу нет) |

**Ограничение:** сканировались локально собранные образы; серверные — просканировать
той же командой Trivy на сервере после каждого обновления.

---

## Приоритетный план исправлений

### Backlog (до стабилизации / перед GA)

1. **L9** — `USER app` в финальном stage Dockerfile (+ права на volume `./keys`, `/app/logs`).
2. **L10** — исключить `appsettings.Development.Local.json` из контекста сборки (`.dockerignore`).
3. **L11** — обновить базу `nginx` до стабильной **1.30.7** (`FROM mirror.gcr.io/library/nginx:1.30-alpine`) и пересобрать web-образ: закрывает CVE-2026-111497 + CVE-2026-31789 (3 CRITICAL) и 67 HIGH; образ просканирован 2026-11-19 → 0 CRITICAL/HIGH.
4. **L12** — `docker pull postgres:16-alpine` (свежая сборка апстрима): 1 CRITICAL (CVE-2025-68121, gosu/Go stdlib) + 56 HIGH; после пулла — повторный скан.
5. **L13** — `react-router-dom` 6 → 7 (breaking, отдельная задача): 2 moderate.

### Закрытые (не в backlog)

- **L2** ✅ — 403 vs 404 existence oracle (фильтрация по visible tenants)
- **L3** ✅ — ImageSharp → SkiaSharp (2026-11-17)

---

## Ограничения аудита

- Scope: **backend Stage 1+2**. Mini App frontend, infrastructure-as-code beyond docker-compose, CI/CD, runtime hardening (SELinux/AppArmor, network policies) — не покрывались.
- Не выполнялись: dynamic testing, exploit PoC, load/fuzz, secret scanning git history beyond working tree.
- Находки опираются на код в working tree на дату аудита; Stage 3 (JWT, bot, webhooks) добавит новые surface — нужен re-audit.
- Severity — по влиянию на confidentiality/integrity/availability с учётом multi-tenant SaaS-модели; commercial license (L3) — compliance, не exploit.

---

## Changelog

### 2026-11-19 — Номера критических CVE (L11), повторный скан (L12), проверка nginx stable

| Что | Детали |
|-----|--------|
| L11 дополнен | CVE-номера из отчёта Trivy: **CVE-2026-111497** (brotli, CVSS 9.8) и **CVE-2026-31789** (openssl, CVSS 9.8) — «3 CRITICAL» = 3 пакетные строки = 2 уникальных CVE; таблица «Критические CVE» с пакетами/версиями/фиксами |
| L12 обновлён | Повторный скан на базе CVE 2026-11-19 07:10: **1 CRITICAL — CVE-2025-68121** (Go stdlib crypto/tls в gosu 1.24.6) + HIGH 31→56; severity Low→Medium |
| nginx stable | Стабильная ветка сегодня — **1.30.7** (`mirror.gcr.io/library/nginx:1.30-alpine`, alpine 3.24.4): скан → **0 CRITICAL / 0 HIGH**, обе критические L11 закрыты; фикс L11 = `FROM … nginx:1.30-alpine` |
| Не изменилось | api-образ 0/0; NuGet 0; npm 2 moderate (L13) |

### 2026-11-18 — Сканирование контейнеров Trivy (L11–L13)

| ID | Кратко | Файлы |
|----|--------|-------|
| **L11** (Medium) | `samorodinkatechcarousel-web` (nginx:1.27-alpine): **3 CRITICAL + 67 HIGH**, все с фиксами → обновить базу nginx | `Dockerfile` |
| **L12** (Low) | `postgres:16-alpine`: **31 HIGH** (musl/openssl/krb5/zlib/libxslt + gosu), все с фиксами → свежий pull тега | `docker-compose.yml` |
| **L13** (Low) | npm: `react-router-dom@6` — 2 moderate; фикс = major 7 (breaking) | `package.json` |
| Чисто | `carousel-api`: Ubuntu 24.04 + .NET 10.0.15 — **0 HIGH/CRITICAL**; NuGet — 0 уязвимых | — |

**Verification:** Trivy 0.76.2 (свежая база CVE), `dotnet list package --vulnerable`, `npm audit --omit=dev`.

### 2026-11-17 — Обследование контейнерного развёртывания (L9–L10)

| ID | Кратко | Файлы |
|----|--------|-------|
| **L9** | Контейнер запускается от root (`uid=0`) → фикс `USER app` | `Dockerfile` |
| **L10** | `appsettings.Development.Local.json` попадает в слой образа (в Production не активируется) → исключить из контекста | `.dockerignore` |
| Контролы | Секреты вне образа (`.dockerignore`/`.env`), порты 127.0.0.1, healthcheck PG, volumes `logs`/`keys`, ротация логов, fail-fast конфига, TLS внешним Caddy | `Dockerfile`, `docker-compose.yml`, `docker/nginx.conf` |

**Verification:** `dotnet publish` (состав артефакта проверен), `docker exec id`, `docker compose config`.

### 2026-11-17 — Закрыт L3 (ImageSharp → SkiaSharp)

| Что | Детали |
|-----|--------|
| Пакеты | Удалены `SixLabors.ImageSharp` 4.1.3, `SixLabors.Fonts` 3.2.0, `SixLabors.ImageSharp.Drawing` 3.1.3; добавлен **`SkiaSharp` 4.156.1** (Apache-2.0) |
| `BackgroundGenerator.cs` | Переписан: `Color`/`Rgba32`/`SaveAsPng` → `SKColor`/`SKBitmap`/`SKImage.Encode(Png)`; попиксельная математика градиента/хайлайта/полос сохранена 1:1 |
| `TemplateBackgroundRenderer.cs` | `Color.ParseHex` → `SKColor.Parse` со своим нормализацией hex |
| `ImageSharpImageProbe.cs` | **Удалён** (в DI и так `HeaderImageProbe`); `ImageProbeTests` переведены на `HeaderImageProbe`, фикстуры PNG/JPEG генерируются SkiaSharp |
| Комментарий DI | `InfrastructureServiceCollectionExtensions` — убрана отсылка к `ImageSharpImageProbe` |
| Документация | `README.md`, `docs/architecture.md` — стек/валидация/шаблоны обновлены на SkiaSharp |

**Verification:** `dotnet build` — 0 ошибок / 0 предупреждений (Six Labors warning'и исчезли); `dotnet test` — **74/74 green** (13 Domain + 51 Application + 10 Integration); **`dotnet publish -c Release` собирается** (бывший блокер).

### 2026-11-17 — L3: детализация и варианты устранения

| Что | Детали |
|-----|--------|
| L3 пересмотрен | Release-сборка **падает** без лицензии Six Labors → классифицирован как **блокер деплоя**, а не только compliance |
| Анализ использования | Runtime: только `BackgroundGenerator`/`TemplateBackgroundRenderer` (попиксельные PNG-фоны, без рисования текста/фигур). Прода-проб изображений — `HeaderImageProbe` без ImageSharp |
| Мусорные пакеты | `SixLabors.Fonts`, `SixLabors.ImageSharp.Drawing` не используются в коде — их build-targets и поднимает ошибку лицензии |
| Варианты | (1) купить лицензию, (2) миграция на SkiaSharp — **рекомендуется**, (3) свой PNG-энкодер на `ZLibStream`; вариант «снять build-gate» запрещён как обход лицензии |

**Verification:** правка только `docs/security-audit.md`; код не менялся.

### 2026-10-19 — Закрыт L2

| ID | Кратко | Файлы |
|----|--------|-------|
| **L2** | Cross-tenant / not-found → **404** (не 403). Фильтрация по `VisibleTenantIdsAsync` в `FindPublicationAsync`, `PlatformAccountEndpoints` delete, `MediaEndpoints` GET — existence oracle закрыт. | `EndpointHelpers.cs`, `PlatformAccountEndpoints.cs`, `MediaEndpoints.cs` |

**Verification:** build 0 ошибок; 64/64 тестов green (13 Domain + 43 Application + 8 Integration).

### 2026-10-19 — Закрыты L7, L8

| ID | Кратко | Файлы |
|----|--------|-------|
| **L7** | Reorder: требую полный set card IDs (`Count` + все `Contains`) → иначе `ValidationException` | `PublicationEndpoints.cs` |
| **L8** | DataProtection: `PersistKeysToFileSystem` в configurable path (`DataProtection:KeyPath`, default `./keys`), `SetApplicationName("Carousel")` | `InfrastructureServiceCollectionExtensions.cs` |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Закрыты L4, L5, L6

| ID | Кратко | Файлы |
|----|--------|-------|
| **L4** | `FindPublicationAsync`: убран unused `userId` param (misleading); access check — ответственность caller'а | `EndpointHelpers.cs`, `PublicationEndpoints.cs` (10 callers) |
| **L5** | Media upload: `file.Length > 12 MB` → reject до copy в MemoryStream | `MediaEndpoints.cs` |
| **L6** | Уже закрыт (audit actor = `actor.User.UserId` в member ops) | — |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Закрыт L1

| ID | Кратко | Файлы |
|----|--------|-------|
| **L1** | 401 message → generic «Not authenticated.» (debug-header hint убран из client response) | `EndpointHelpers.cs` |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Закрыт M6

| ID | Кратко | Файлы |
|----|--------|-------|
| **M6** | `BotConversationEngine.AdvanceAsync`: payload > 32 KB → `ValidationException` до записи в DB | `BotConversationEngine.cs` |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Закрыт M2

| ID | Кратко | Файлы |
|----|--------|-------|
| **M2** | `InvalidOperationException` → generic 400 «Invalid operation.» (без raw Message); `Detail` — только для known business exceptions; остальное server-side log | `Program.cs` |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Закрыты M3, M5

| ID | Кратко | Файлы |
|----|--------|-------|
| **M3** | ForwardedHeaders (X-Forwarded-For/Proto); security headers middleware (nosniff, X-Frame-Options DENY, Referrer-Policy, Permissions-Policy, CSP `default-src 'none'`); `UseHttpsRedirection` + `UseHsts` в Production | `Program.cs` |
| **M5** | Дубль H6 (done) — закрыт автоматически | — |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Закрыт H6

| ID | Кратко | Файлы |
|----|--------|-------|
| **H6** | Auto-migrate gate (`Database:AutoMigrate` / IsDevelopment); C# fallback → fail fast в Production; compose `127.0.0.1:5432` + `${POSTGRES_PASSWORD}`; `.env.example`; `.env` в gitignore | `Program.cs`, `InfrastructureServiceCollectionExtensions.cs`, `appsettings.json`, `docker-compose.yml`, `.env.example`, `.gitignore` |

**Verification:** build 0 ошибок; 59/59 тестов green; `docker compose config` валиден.

### 2026-10-19 — Закрыт H4

| ID | Кратко | Файлы |
|----|--------|-------|
| **H4** | `RetryAsync`: добавлены `EnsureCanPublishAsync` + `ReservePublicationAsync` до delivery + `ReleasePublicationAsync` при полном failure | `PublishCoordinator.cs` |

**Verification:** build 0 ошибок; 59/59 тестов green.

### 2026-10-19 — Миграция .NET 10 / EF Core 10

| Что | Детали |
|-----|--------|
| TFM | net9.0 → **net10.0** (`Directory.Build.props`) |
| EF Core | 9.0.4 → **10.0.13**; `ExecuteSqlRawAsync` → `ExecuteSqlAsync` (FormattableString) |
| Npgsql | 9.0.4 → **10.0.3** |
| Serilog / Swashbuckle / ASP.NET | → 10.x / 10.3.0 / 10.0.13 |
| IdentityModel | 8.14.0 → **8.23.0** (security patch) |
| Testcontainers | 4.3.0 → **4.16.0** — **закрыт SSH.NET CVE** (2× High) |
| Результат | 0 vulnerabilities; 59/59 tests green на net10.0 |

### 2026-10-19 — Закрыты C3, H2, H3, M1, M4

| ID | Кратко | Файлы |
|----|--------|-------|
| **C3** | Webhooks: provider allowlist + `Billing:WebhooksEnabled` config-gate (default `false`) | `BillingEndpoints.cs`, `appsettings.json` |
| **H2** | DI → `HeaderImageProbe` (dimensions из header, без полного pixel decode) | `InfrastructureServiceCollectionExtensions.cs` |
| **H3** | Атомарный quota reserve: `TryReserveMonthlyPublicationAsync` (INSERT + conditional UPDATE) до delivery; `ReleasePublicationAsync` при полном failure | `EfBillingQuery.cs`, `LimitService.cs`, `PublishCoordinator.cs` |
| **M1** | `RequireMembershipAsync(..., Editor)` на `POST /media` — Viewer получает 403 | `MediaEndpoints.cs` |
| **M4** | `UpdatePublicationRequestValidator` + `UpdateCardRequestValidator`; wired в PATCH endpoints | `Validators.cs`, `PublicationEndpoints.cs` |

**Verification:** build 0 ошибок; 59/59 тестов green (13 Domain + 39 Application + 7 Integration).

---

## Связанные документы

- [docs/architecture.md](architecture.md) — архитектура Stage 2, RBAC matrix  
- [docs/api-contracts.md](api-contracts.md) — HTTP-контракты и коды ошибок  
- [docs/data-model.md](data-model.md) — модель данных  
