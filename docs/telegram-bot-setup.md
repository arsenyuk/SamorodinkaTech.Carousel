# Регистрация Telegram-бота и ключ (токен)

Короткая инструкция: как создать бота в Telegram и куда вписать токен для Carousel.

## 1. Создать бота

1. Откройте Telegram, найдите **[@BotFather](https://t.me/BotFather)** и начните диалог.
2. Отправьте команду:

   ```
   /newbot
   ```

3. BotFather попросит:
   - **имя бота** — как отображается в Telegram (например `Carousel Bot`);
   - **username** — уникальный, обязательно заканчивается на `bot` (например `my_carousel_bot`).
4. BotFather пришлёт **токен** вида:

   ```
   123456789:AAE5aCInjIU9ywQIk8p0M2qHXYX1Nxc27ZY
   ```

   Это и есть **ключ** (API token). Без него бот не работает.

> **Важно:** не публикуйте токен и не коммитьте его в git. Если токен засветился — перевыпустите через `/revoke` в BotFather.

## 2. Куда вписать ключ

### Вариант A — файл `.env` (локальная разработка, recommended)

1. В корне репозитория (рядом с `.env.example`) создайте или откройте файл `.env`.
2. Найдите строку:

   ```
   Telegram__BotToken=
   ```

3. Впишите токен **после** `=`, без пробелов и кавычек:

   ```
   Telegram__BotToken=123456789:AAE5aCInjIU9ywQIk8p0M2qHXYX1Nxc27ZY
   ```

4. Сохраните файл. `.env` уже в `.gitignore` — в git он не попадает.

### Вариант B — переменные окружения (сервер / Docker)

```bash
export Telegram__BotToken="123456789:AAE5..."
# или в docker-compose / systemd / .env для compose
```

### Вариант C — appsettings (не рекомендуется)

В `src/Carousel.Api/appsettings.json` поле `Telegram:BotToken` оставляйте **пустым**.  
Секреты в git-трекаемых файлах хранить нельзя (правило AGENTS.md).

## 3. Отключить mock и запустить

В Development по умолчанию включён **mock-бот** (`Telegram:UseMock=true`) — живой токен не нужен.

Для **настоящего** бота:

1. В `.env` или окружении:

   ```
   Telegram__BotToken=<ваш_токен>
   Telegram__UseMock=false
   ```

2. Запустите API:

   ```bash
   dotnet run --project src/Carousel.Api
   ```

3. В логах должно появиться сообщение о старте long polling (не mock).

## 4. Проверка

1. Найдите своего бота в Telegram по username.
2. Отправьте `/start`.
3. Бот должен ответить (диалог создания карусели / приветствие).
4. Mini App: откройте Mini App из ссылки бота или локально `http://localhost:5173` (Dev login).

## Частые проблемы

| Симптом | Причина | Что сделать |
|---------|---------|-------------|
| Бот молчит | `UseMock=true` или пустой токен | `Telegram__UseMock=false`, проверьте `Telegram__BotToken` |
| `409 Conflict` | Long polling уже идёт с другого процесса | Остановите второй экземпляр API |
| `401 Unauthorized` | Токен неверный / отозван | Проверьте строку у BotFather, при необходимости `/revoke` и `/newbot` |
| Mini App не открывается | Не настроен `Telegram:MiniAppUrl` | Укажите публичный HTTPS-URL Mini App |

## Ссылки

- [BotFather](https://t.me/BotFather)
- [Telegram Bot API](https://core.telegram.org/bots/api)
- [.env.example](../.env.example)
