# Публикация MyaK Biplane

## Что куда

| Часть | Проект | Куда публиковать |
| --- | --- | --- |
| Клиент (меню, Canvas, HUD) | `MyaKBiplane.Client` | GitHub Pages |
| Сервер (симуляция, бот, комнаты, SignalR) | `MyaKBiplane.Server` | Docker-хостинг с HTTPS и WebSocket |

GitHub Pages раздаёт только статические файлы, поэтому сервер там работать не может.
Из решения на Pages попадает **только `MyaKBiplane.Client`** — опубликованный каталог
`wwwroot` после `dotnet publish`.

Сервер обязателен и для сетевой игры, и для матча с ботом: клиент не считает физику
сам, а рисует снимки, которые присылает `MyaKBiplane.Server`. Без сервера Pages-версия
откроет меню, но бой не запустится.

```
Браузер ── HTTPS ──> GitHub Pages (статика Blazor WASM)
   │
   └──── WSS/HTTPS ──> MyaKBiplane.Server (Docker, SignalR)
```

## 1. Поднять сервер

Сервер должен быть доступен по **HTTPS**: страница на GitHub Pages тоже HTTPS, а браузер
заблокирует подключение к `http://` как mixed content. Прокси/хостинг должен поддерживать
WebSocket — SignalR использует его для соединения.

### Вариант A. Fly.io (выбранный)

В корне репозитория лежит `fly.toml`. Он собирает `MyaKBiplane.Server/Dockerfile`,
слушает внутренний порт `8080`, включает HTTPS и health-check `/health`.

```powershell
# Установка CLI (один раз).
iwr https://fly.io/install.ps1 -useb | iex
$flyctl = "$env:USERPROFILE\.fly\bin\flyctl.exe"

# Вход: откройте предложенную ссылку в браузере.
& $flyctl auth login

# Приложение создаётся один раз.
& $flyctl apps create myak-biplane-server

# Разрешить запросы с GitHub Pages.
& $flyctl secrets set Cors__AllowedOrigins__0=https://ivanmyak.github.io --app myak-biplane-server

# Деплой из корня репозитория.
& $flyctl deploy --app myak-biplane-server --ha=false --yes
```

Адрес приложения: `https://myak-biplane-server.fly.dev`.
Проверка: `https://myak-biplane-server.fly.dev/health` → `{"status":"ok"}`.

`--ha=false` создаёт одну машину вместо двух — для игры этого достаточно и дешевле.
Комнаты и матчи хранятся в памяти процесса, поэтому несколько машин без sticky-сессий
не подойдут: оставляйте одну.

Регион задан в `fly.toml` (`primary_region = "arn"`, Стокгольм). Некоторые старые
регионы, например `waw`, Fly.io закрыл для новых приложений — при ошибке
`Region ... is deprecated` выберите регион из подсказки в тексте ошибки.

При `min_machines_running = 0` машина засыпает без трафика и просыпается на первый
запрос; для постоянной готовности поставьте `min_machines_running = 1`.

Логи сервер пишет в `/app/Data/Logs` внутри контейнера. Это временный диск машины:
после перезапуска файлы теряются. Если логи нужны надолго, создайте volume
(`fly volumes create myak-data --size 1 --region arn`) и смонтируйте его в `fly.toml`:

```toml
[mounts]
  source = "myak-data"
  destination = "/app/Data"
```

Бесплатный trial на Fly.io действует ограниченное время. По его окончании нужно
добавить способ оплаты, иначе приложение остановят.

### Вариант B. Render

1. Создайте **New → Web Service** и подключите репозиторий `ivanmyak/MyaK-Biplane`.
2. Укажите **Language**: Docker, **Dockerfile Path**: `MyaKBiplane.Server/Dockerfile`,
   **Docker Context**: `.` (корень репозитория), **Health Check Path**: `/health`.
3. В **Environment** добавьте `Cors__AllowedOrigins__0` = `https://ivanmyak.github.io`.
4. Задеплойте и проверьте `https://myak-biplane.onrender.com/health`.

### Вариант C. Свой VPS с Docker и Caddy

```bash
git clone https://github.com/ivanmyak/MyaK-Biplane.git
cd MyaK-Biplane
docker build -f MyaKBiplane.Server/Dockerfile -t myak-biplane-server .

docker run -d \
  --name myak-biplane-server \
  --restart unless-stopped \
  -p 127.0.0.1:8080:8080 \
  -e Cors__AllowedOrigins__0=https://ivanmyak.github.io \
  -v myak-biplane-logs:/app/Data/Logs \
  myak-biplane-server
```

Caddy сам получит сертификат и проксирует WebSocket:

```caddyfile
game.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

Проверьте `https://game.example.com/health`.

### Переменные окружения сервера

| Переменная | Значение |
| --- | --- |
| `Cors__AllowedOrigins__0` | `https://ivanmyak.github.io` — origin Pages. Для своего домена укажите его. |
| `PORT` | Необязательно. Если хостинг передаёт порт через `PORT`, сервер его использует. |
| `ASPNETCORE_URLS` | В Docker уже задан `http://+:8080`; менять не нужно. |

`Cors__AllowedOrigins__0` можно не задавать: origin `https://ivanmyak.github.io` уже
добавлен в `MyaKBiplane.Server/appsettings.json`. Для форка или своего домена
обязательно переопределите его через переменную окружения.

## 2. Настроить GitHub Pages

1. Откройте **Settings → Pages**.
2. В **Build and deployment → Source** выберите **GitHub Actions**.
3. Откройте **Settings → Secrets and variables → Actions → Variables** и создайте
   переменную:
   - **Name**: `SERVER_BASE_URL`;
   - **Value**: публичный HTTPS-адрес сервера без завершающего слэша и без `/hubs`,
     например `https://myak-biplane-server.fly.dev`.
4. Смержите изменения в `master` или запустите workflow вручную:
   **Actions → Deploy client to GitHub Pages → Run workflow**.

Workflow `.github/workflows/deploy-pages.yml`:

- собирает `MyaKBiplane.Client` в Release;
- подставляет `<base href="/MyaK-Biplane/" />` для project-сайта;
- записывает `SERVER_BASE_URL` в `wwwroot/appsettings.json`;
- создаёт `404.html` (fallback для глубоких ссылок вроде `/play`) и `.nojekyll`;
- публикует статику через `actions/deploy-pages`.

Готовый адрес: **https://ivanmyak.github.io/MyaK-Biplane/**.

### Если репозиторий переименован

Измените `PAGES_BASE_PATH` в начале workflow. Для репозитория вида
`<user>.github.io` укажите `/`, иначе — `/<имя-репозитория>/`.

## 3. Проверка после деплоя

1. Откройте `https://ivanmyak.github.io/MyaK-Biplane/`.
2. В меню переключите светлую и тёмную тему — выбор сохранится после перезагрузки.
3. Нажмите «Игра с ботом» → «Начать бой с ботом» — должен открыться Canvas и матч.
4. Откройте «Игра по сети» → «Создать комнату» — появится код комнаты.
5. Обновите страницу на `/MyaK-Biplane/play?mode=bot&kind=biplane` — благодаря
   `404.html` приложение должно загрузиться, а не отдать 404.

## Частые проблемы

- **Меню открылось, бой пишет «Не удалось войти в матч».** Не задан `SERVER_BASE_URL`,
  сервер спит/не запущен, либо сервер по HTTP. Проверьте `/health` и HTTPS.
- **Ошибка CORS в консоли.** В `Cors__AllowedOrigins__0` должен быть точный origin
  без пути: `https://ivanmyak.github.io`.
- **Пустая страница или 404 на `/MyaK-Biplane/`.** Проверьте, что `PAGES_BASE_PATH`
  совпадает с именем репозитория и что Pages публикует артефакт Actions.
- **Соединение обрывается через минуту.** Прокси не пропускает WebSocket или режет
  долгие соединения. Для nginx нужны `proxy_set_header Upgrade`/`Connection` и
  увеличенные таймауты — пример есть в `MyaKBiplane.Client/nginx.conf`.
- **После обновления страницы 404.** В артефакте нет `404.html`; workflow создаёт его
  автоматически, проверьте лог шага «Add SPA fallback».
