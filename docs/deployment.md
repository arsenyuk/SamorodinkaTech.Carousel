# Инструкция по поставке (Docker)

Контейнерная поставка Carousel: два образа (`api` + `web`), данные — в volume PostgreSQL.

---

## Шаг 0 — Установить Docker на сервере (Ubuntu, один раз)

Если Docker уже установлен — пропустите шаг.

```bash
# официальный репозиторий Docker для Ubuntu/Debian
curl -fsSL https://get.docker.com | sudo sh

# чтобы работал `docker compose` (плагин входит в установку) и не нужен sudo:
sudo usermod -aG docker $USER
newgrp docker

# проверка
docker version
docker compose version
```

Без `newgrp docker`/перелогина команды `docker` будут отвечать
`permission denied`.

---

## Шаг 1 — Собрать образы (машина разработки)

```bash
cd ~/Проекты/SamorodinkaTech.Carousel
docker compose --profile full build
docker save samorodinkatechcarousel-api samorodinkatechcarousel-web -o carousel-images.tar
```

Готово: `carousel-images.tar` — все образы для сервера.

## Шаг 2 — Скопировать на сервер 4 файла

| Файл | Что это |
|------|---------|
| `docker-compose.yml` | описание сервисов (api, web, postgres) |
| `docker/nginx.conf` | конфиг nginx (статика + прокси `/api`) |
| `.env` | секреты: `POSTGRES_PASSWORD`, `Auth__Jwt__SigningKey`, `Telegram__BotToken` |
| `carousel-images.tar` | архив образов |

```bash
scp docker-compose.yml docker/nginx.conf .env carousel-images.tar user@server:/opt/carousel/
```

> `.env` — единственный файл с секретами. В git не попадает. На сервере — свои значения, не dev-заглушки.

## Шаг 3 — Запустить на сервере

```bash
cd /opt/carousel
docker load -i carousel-images.tar
docker compose --profile full up -d
```

## Проверка

```bash
curl http://localhost:8081/health     # → {"status":"ok"}
docker ps                             # postgres (healthy), api, web — Up
docker logs carousel-api --tail 20    # нет ошибок, бот в поллинге
```

Открыть в браузере: `http://localhost:8081`

---

## Обновление

```bash
# 1. На машине разработки: шаг 1 заново (новый carousel-images.tar)
# 2. Скопировать новый tar на сервер
# 3. На сервере:
cd /opt/carousel
docker load -i carousel-images.tar
docker compose --profile full up -d   # поднимет контейнеры с новым образом
```

Данные (публикации, картинки, шаблоны) **не теряются** — они в volume PostgreSQL
(`carousel-postgres-data`), контейнеры пересоздаются, volume остаётся.

---

## Полезные команды

```bash
docker compose --profile full up -d       # запуск
docker compose --profile full down        # остановить (volume с данными остаётся)
docker compose --profile full build       # пересобрать образы из кода
docker compose --profile full logs -f api # логи API
docker compose --profile full ps          # статус сервисов
```

Без профиля `full` (`docker compose up -d`) поднимается **только PostgreSQL** —
так удобно работать в dev, когда API запускается через `./scripts/dev-api.sh`.

---

## Порты

| Порт | Сервис |
|------|--------|
| `127.0.0.1:8081` | web (nginx): Mini App + прокси `/api` |
| `127.0.0.1:8080` | api напрямую (для диагностики) |
| `127.0.0.1:5432` | PostgreSQL |

## Переменные окружения (`.env`)

| Переменная | Назначение |
|------------|-----------|
| `POSTGRES_PASSWORD` | пароль БД (должен совпадать с паролем в volume!) |
| `Auth__Jwt__SigningKey` | ключ подписи JWT, **минимум 32 символа**, в проде — уникальный |
| `Telegram__BotToken` | токен бота от @BotFather |
| `VITE_API_BASE_URL` | адрес API для сборки фронта (default `http://localhost:8081`) |
| `Cleanup__Enabled` | фоновая retention-очистка БД (default `true`) |
| `Cleanup__PublicationRetentionDays` | удалять публикации старше N дней (default `180`) |
| `Cleanup__OrphanMediaRetentionDays` | удалять осиротевшие картинки старше N дней (default `30`) |
| `Cleanup__IntervalHours` | период прогона в часах (default `24`) |

## Что учитывать на боевом сервере

### 1. HTTPS и домен (обязательно для Telegram Mini App)

Telegram открывает Mini App только по `https://`-адресу. Сейчас `web` слушает
`127.0.0.1:8081` — снаружи сервера он недоступен. Перед ним ставится
reverse-proxy с сертификатом. Самый простой вариант — **Caddy** (сертификаты
выпускает и продлевает сам):

```bash
# на сервере
apt install -y debian-archive-keyring apt-transport-https ca-certificates curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
  | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
  | tee /etc/apt/sources.list.d/caddy-stable.list
apt update && apt install caddy
```

Конфиг `/etc/caddy/Caddyfile`:

```
carousel.example.ru {
    reverse_proxy 127.0.0.1:8081
}
```

```bash
systemctl reload caddy
```

Caddy сам выпустит сертификат Let's Encrypt (нужен открытый порт 80/443 и
домен, указанный на IP сервера). Дальше:

- в `.env` сервера: `CORS_ORIGIN=https://carousel.example.ru` (не обязателен,
  если фронт и API на одном домене — но нужен, если разводить);
- в `docker-compose.yml` web-сервиса: `VITE_API_BASE_URL=https://carousel.example.ru`
  (пересобрать web-образ с этим аргументом — путь запросов фронта зашивается
  при **сборке**);
- в @BotFather: `/mybots` → Bot Settings → Menu Button / Mini App → задать
  `https://carousel.example.ru`.

Порты контейнеров **намеренно привязаны к 127.0.0.1** — наружу торчат только
80/443 у Caddy. Проверить: `curl -I https://carousel.example.ru/health`.

### 2. Миграции БД

**Правило (AGENTS): авто-миграции в Production всегда отключены**
(`Database__AutoMigrate="false"` в compose, `AutoMigrate=false` в базовом
`appsettings.json`). Миграции применяются **только по команде владельца** —
включая первое развёртывание.

Порядок:

1. Релиз **без новых миграций** → обычный деплой (`docker compose up -d`) — ничего
   дополнительного делать не нужно.
2. Релиз **с новыми миграциями** → **сначала применить их, потом поднимать API**:

```bash
# каталог с исходниками ТОЙ ЖЕ версии, что уезжает в бой
docker run --rm -v "$PWD":/src -w /src \
  -e ConnectionStrings__Carousel="Host=postgres;Port=5432;Database=carousel;Username=carousel;Password=***" \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c \
  "dotnet tool install --global dotnet-ef && export PATH=\$PATH:/root/.dotnet/tools \
   && dotnet ef database update --project src/Carousel.Api"
docker compose --profile full up -d
```

⚠️ Деплой кода с новыми миграциями **до** их применения невозможен — приложение
упадёт на обращении к отсутствующим колонкам. Сначала миграции, потом API.

В Development (`./scripts/dev-api.sh`) `AutoMigrate=true` в
`appsettings.Development.json` — локальная разработка остаётся удобной.
Руками мигрировать prod-БД **с dev-машины нельзя** (dev-код может отличаться
от прод-версии).

### 3. Версии образов и откат

Собирайте с тегом git-сводки, а не `:latest` — тогда откат = поднять прошлый
тег. Работает и с tar-схемой, реестр образов не нужен. Если применяете этот
вариант, команды ниже **заменяют «Шаг 1»** из начала инструкции (в `docker save`
будут имена `carousel-api:$TAG` вместо `samorodinkatechcarousel-api:latest`):

```bash
TAG=$(git rev-parse --short HEAD)
docker tag samorodinkatechcarousel-api:latest carousel-api:$TAG
docker tag samorodinkatechcarousel-web:latest  carousel-web:$TAG
docker save carousel-api:$TAG carousel-web:$TAG -o carousel-images-$TAG.tar
```

В `docker-compose.yml` закрепите тег через переменную:

```yaml
api:
  image: carousel-api:${TAG:-latest}
  build:
    context: .
    target: api
```

На сервере (образ уже загружен через `docker load`, исходников нет —
поэтому `--no-build`):

```bash
TAG=<sha> docker compose --profile full up -d --no-build
```

Откат — тот же шаг с прошлым тегом:

```bash
TAG=<предыдущий sha> docker compose --profile full up -d --no-build
```

### 4. Бэкапы данных

Всё содержимое (публикации, картинки, шаблоны, пользователи) — в volume
`carousel-postgres-data`. Без бэкапа = потеря при удалении volume.

```bash
# бэкап (ежедневно по cron)
docker exec carousel-postgres pg_dump -U carousel -Fc carousel \
  > backup/carousel-$(date +%F).dump

# восстановление
docker exec -i carousel-postgres pg_restore -U carousel -d carousel \
  --clean --if-exists < backup/carousel-2026-11-17.dump
```

Хранить бэкапы вне сервера (S3/другая машина).

### 5. Секреты

```bash
# генерация JWT-ключа (32+ байта)
openssl rand -base64 48
```

- `.env` на сервере — отдельный, свои значения; в git не коммитить, в архив
  образов не попадает.
- Токен бота и JWT-ключ **не совпадают** с dev-машиной.
- Ротация: поменяли значение в `.env` → `docker compose --profile full up -d`
  (api перечитает env при пересоздании). После смены JWT-ключа все выданные
  токены перестают действовать — пользователи залогинятся заново.

### 6. Логи и диски

- Логи приложения: `./logs/` на хосте (volume api-контейнера). Ротация
  настроена в `appsettings.json`: файл роятся **по дням** (`carousel-api-20261117.log`),
  размер одного файла ограничен **100 МБ** (дальше `..._001.log`), файлы старше
  **14 дней** Serilog удаляет **автоматически** при старте приложения и при
  ролле. Ручная очистка и logrotate не нужны.
- Логи также идут в консоль (`docker logs carousel-api`) — смотреть их удобнее,
  чем файлы: `docker compose --profile full logs -f api`.
- Docker-логи контейнеров (stdout): по умолчанию unbounded → включить лимит:
  в `/etc/docker/daemon.json` → `"log-driver": "json-file",
  "log-opts": {"max-size": "50m", "max-file": "3"}` и перезапустить docker.
- Диск volume: `docker system df -v` — следить за `carousel-postgres-data`
  и `./logs`.

### 7. Обновление без потери данных — чек-лист

```bash
# На машине разработки:
1. TAG=$(git rev-parse --short HEAD) && docker compose --profile full build
2. docker tag ... carousel-api:$TAG && docker tag ... carousel-web:$TAG
3. docker save carousel-api:$TAG carousel-web:$TAG -o carousel-images-$TAG.tar

# На сервере:
4. scp carousel-images-$TAG.tar user@server:/opt/carousel/
5. docker load -i carousel-images-$TAG.tar
6. TAG=$TAG docker compose --profile full up -d --no-build
7. curl https://carousel.example.ru/health  → {"status":"ok"}
8. docker logs carousel-api --tail 30      → без ошибок
```

Миграции в контейнере отключены (`AutoMigrate=false`) — применяются по команде
владельца (см. раздел «Миграции БД»). Volume PostgreSQL при этих шагах не
трогается — данные целы.

### 8. Особенности сборки из этой сети

- **docker.io недоступен** — official-образы (node/nginx) берутся через
  `mirror.gcr.io`, это уже прописано в Dockerfile. На сервере с доступом к
  Hub можно собирать и напрямую.
- Образы .NET (mcr.microsoft.com) доступны как есть.