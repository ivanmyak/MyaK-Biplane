# Публикация сервера MyaK Biplane на Fly.io

На Fly.io размещается **только сервер** `MyaKBiplane.Server` (симуляция, бот, комнаты, SignalR).
Клиент `MyaKBiplane.Client` выкладывается на GitHub Pages — инструкция в
[`deployment.md`](deployment.md). Без сервера меню откроется, но матч не запустится.

```
Telegram / браузер ── HTTPS ──> GitHub Pages (Blazor WASM)
                                  │
                                  └── WSS/HTTPS ──> Fly.io (SignalR, /hubs/game)
```

## 1. Подготовка

Установить flyctl и войти (один раз):

```powershell
iwr https://fly.io/install.ps1 -useb | iex
$fly = "$env:USERPROFILE\.fly\bin\flyctl.exe"

& $fly auth login          # откроется браузер
& $fly auth whoami         # проверка
```

Приложение уже создано (`myak-biplane-server`). Если нужно создать заново:

```powershell
& $fly apps create myak-biplane-server
```

## 2. Источник правды — `fly.toml` в репозитории

Вкладка **Fly.toml** в дашборде — это просмотр уже применённой конфигурации. Редактировать её
там нельзя (или неудобно): изменения вносятся в файл `fly.toml` **в корне репозитория** и
применяются командой `flyctl deploy`. Вся рабочая конфигурация берётся оттуда.

Текущий файл в репозитории:

```toml
app = "myak-biplane-server"
primary_region = "arn"

[build]
  dockerfile = "MyaKBiplane.Server/Dockerfile"

[env]
  ASPNETCORE_ENVIRONMENT = "Production"

[http_service]
  internal_port = 8080
  force_https = true
  auto_stop_machines = "stop"
  auto_start_machines = true
  min_machines_running = 0
  processes = ["app"]

  [http_service.concurrency]
    type = "connections"
    hard_limit = 200
    soft_limit = 150

  [[http_service.checks]]
    grace_period = "10s"
    interval = "30s"
    method = "GET"
    path = "/health"
    timeout = "5s"

[[vm]]
  memory = "512mb"
  cpu_kind = "shared"
  cpus = 1
```

⚠️ **Важно:** конфигурация в дашборде может отличаться от файла в репозитории — например, там
указан `primary_region = "fra"`, а в файле `"arn"`, и `auto_stop_machines = true` вместо `"stop"`.
Перед деплоем сверь их: `flyctl deploy` **перезапишет** рабочую конфигурацию локальным файлом,
в том числе регион.

### Что означают поля

| Поле | Смысл |
| --- | --- |
| `primary_region` | Регион machines. Для игрока из РФ `arn` (Стокгольм) или `hel` (Хельсинки) обычно лучше, чем `fra`. |
| `dockerfile` | Сборка сервера из `MyaKBiplane.Server/Dockerfile`, контекст — корень репозитория. |
| `internal_port = 8080` | Порт внутри контейнера; совпадает с `ASPNETCORE_URLS` в Dockerfile. Менять не нужно. |
| `force_https` | Fly сам выдаёт сертификат и редиректит HTTP → HTTPS. Клиент работает по HTTPS — обязательно. |
| `auto_stop_machines` | Разрешает останавливать машину при простое. |
| `auto_start_machines` | Просыпаться на первый запрос. |
| `min_machines_running` | Сколько машин держать тёплыми. `0` = экономно, но холодный старт; `1` = без задержки при входе. |
| `[[http_service.checks]]` | Health-check `/health`; если падает — Fly перезапустит машину. |
| `[[vm]]` | 1 shared CPU, 512 MB — одного достаточно, комнат в памяти процесса. |

## 3. Секреты (главное — CORS)

Секреты **не лежат в `fly.toml`**. Посмотреть и задать:

```powershell
& $fly secrets list --app myak-biplane-server
& $fly secrets set Cors__AllowedOrigins__0=https://ivanmyak.github.io --app myak-biplane-server
```

Origin должен быть **точным**, без пути и без завершающего слэша. Для Mini App отдельный CORS не
нужен: он открывает тот же `https://ivanmyak.github.io`. Если сайт на своём домене — укажите его.

Если секрет не задан, запросы из браузера будут заблокированы CORS и матч не запустится.

## 4. Деплой

`flyctl deploy` **всегда читает `fly.toml` из текущей папки** (или из `--config`). Поэтому запускать
команду нужно из корня репозитория — именно там лежит файл. Это единственный источник правды:
всё, что изменилось в дашборде, будет перезаписано значениями из файла.

```powershell
$fly = "$env:USERPROFILE\.fly\bin\flyctl.exe"
Set-Location C:\Users\myaki\source\repos\MyaK-Biplane

# --ha=false: одна машина вместо двух. Комнаты живут в памяти процесса,
# поэтому несколько машин без sticky-сессий не подходят.
& $fly deploy --app myak-biplane-server --ha=false --yes
```

Если файл лежит в другом месте, укажите его явно:

```powershell
& $fly deploy --config .\fly.toml --app myak-biplane-server --ha=false --yes
```

Команда пересобирает Docker-образ из `MyaKBiplane.Server/Dockerfile`, загружает его и обновляет
машины. Если machines удалены — создаёт новую. Изменения в коде сервера подхватываются этой же
командой, отдельно ничего делать не нужно.

После деплоя проверьте:

```powershell
& $fly status --app myak-biplane-server
& $fly logs --app myak-biplane-server --follow
```

и `https://myak-biplane-server.fly.dev/health`.

Сменить регион при деплое без правки файла:

```powershell
& $fly deploy --app myak-biplane-server --primary-region hel --ha=false --yes
```

## 5. Проверка

```powershell
& $fly status --app myak-biplane-server
& $fly logs --app myak-biplane-server --follow
```

Адреса:

- `https://myak-biplane-server.fly.dev/health` → `{"status":"ok"}`
- `https://myak-biplane-server.fly.dev/` → JSON с `name`, `version`, `protocolVersion`, `status`
- `https://myak-biplane-server.fly.dev/ws/ping` → WebSocket-echo для диагностики RTT
- SignalR: `wss://myak-biplane-server.fly.dev/hubs/game` (в Network должно быть `101 Switching Protocols`)

Проверить WebSocket из PowerShell:

```powershell
$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$uri = [Uri]"wss://myak-biplane-server.fly.dev/ws/ping"
$ws.ConnectAsync($uri, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
$ws.State   # Open — значит WSS работает
```

## 6. Полезные команды

```powershell
# текущее состояние машин и регионов
& $fly status --app myak-biplane-server

# перезапуск/остановка/удаление машины
& $fly machine restart <id> --app myak-biplane-server
& $fly machine stop <id> --app myak-biplane-server
& $fly machine destroy <id> --app myak-biplane-server

# посмотреть применённую конфигурацию
& $fly config show --app myak-biplane-server

# количество машин
& $fly scale count 1 --app myak-biplane-server
```

## 7. Типичные проблемы

| Симптом | Причина и решение |
| --- | --- |
| Машина отсутствует после удаления | Выполните `flyctl deploy` — machine создаётся из `fly.toml`. |
| `Region ... is deprecated` | Регион закрыт для новых приложений; выберите из подсказки (`arn`, `hel`, `ams`). |
| Первый вход в игру долгий | Машина спала (`min_machines_running = 0`). Поставьте `1`. |
| Матч пишет «Не удалось войти в матч» | Нет `SERVER_BASE_URL` на Pages, сервер спит или CORS не включает origin Pages. |
| CORS в консоли | `Cors__AllowedOrigins__0` должен быть `https://ivanmyak.github.io` без пути. |
| Обрыв соединения через минуту | Прокси режет WebSocket. Убедитесь, что `force_https = true` и `/hubs/` не кешируется. |
| Конфигурация в дашборде не совпадает с репозиторием | `fly.toml` в дашборде read-only; меняйте локальный файл и делайте `deploy`. |
| Trial закончился | Нужно добавить способ оплаты, иначе машины остановят. |

## 8. Стоимость

`shared-cpu-1x` + 512 MB — минимальная конфигурация. `min_machines_running = 0` почти бесплатен,
но даёт холодный старт; `1` держать машину постоянно дешевле, чем платить за простой при
большом трафике, но стоит денег. Бесплатный trial Fly ограничен по времени.
