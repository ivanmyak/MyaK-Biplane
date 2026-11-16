# MyaK Biplane

Прототип 2D-догфайта: **биплан против птеродактиля**. Первый играбельный срез — оффлайн-матч
против бота в браузере. Симуляция отделена от рендера, поэтому позже на её основе можно
сделать сетевую игру и мобильный клиент.

## Что уже работает

- Детерминированная симуляция `MyaKBiplane.Core`: движение, гравитация, стрельба, урон,
  кулдауны, возрождение, счёт до 5 уничтожений.
- Два класса: биплан (инерция, повороты, ускорение) и птеродактиль (прямое управление, рывок,
  заряжаемое копьё).
- Оффлайн-бот, который играет через тот же `PlayerInput`, что и человек.
- Blazor WASM клиент с HTML5 Canvas: меню, HUD, частицы, экран победы/поражения.
- Сервер-заготовка: health-check, SignalR-хаб `GameHub`, Serilog.
- Docker-оркестрация для release и локального debug.
- Точечные тесты ядра.

## Структура решения

| Проект | Назначение |
| --- | --- |
| `MyaKBiplane.Core` | Детерминированная симуляция матча, баланс, бот. Без зависимостей от UI и сети. |
| `MyaKBiplane.Game` | Клиентская сессия: фиксированный шаг, эффекты, JSON-снимки для рендера. |
| `MyaKBiplane.Client` | Blazor WASM + Canvas. Тонкий слой UI, вся отрисовка в `wwwroot/js/game.js`. |
| `MyaKBiplane.Server` | ASP.NET Core: SignalR, health-check, Serilog. Пока каркас для онлайна. |
| `MyaKBiplane.Shared` | Общие метаданные и версия протокола. |
| `MyaKBiplane.Tests` | xUnit-тесты математики, физики, боя, бота и детерминизма. |

Проекты лежат в корне решения, без папки `src/`.

## Требования

- .NET SDK **10.0.403** или новее в ветке 10.0 (зафиксировано в `global.json`).
- Docker Desktop — для запуска через compose.
- Внешняя сеть `shared-net` и существующий PostgreSQL — только для debug-compose.
  На текущем срезе база не используется, но подключение уже предусмотрено в окружении.

## Локальный запуск без Docker

Терминал 1 — сервер:

```powershell
dotnet run --project MyaKBiplane.Server
```

Сервер поднимется на `http://localhost:5179` (см. `Properties/launchSettings.json`).

Терминал 2 — клиент:

```powershell
dotnet run --project MyaKBiplane.Client
```

Клиент откроется на `http://localhost:5049`. Играть можно и без сервера: клиент считает
матч локально.

## Управление

### Биплан

- `W` / `S` — тяга вперёд / назад
- `A` / `D` — поворот
- `ЛКМ` или `F` — огонь
- `Пробел` — ускорение (действует 5 с, затем кулдаун)
- `R` — реванш после матча
- `Esc` — выход в меню

### Птеродактиль

- `WASD` — движение
- `Мышь` — прицел
- `ЛКМ` — удержание заряжает копьё, отпускание — бросок
- `Пробел` — рывок
- `R` — реванш после матча
- `Esc` — выход в меню

Матч идёт до 5 уничтожений. После уничтожения аппарат возрождается через 2,5 секунды.

## Тесты

```powershell
dotnet test MyaKBiplane.Tests/MyaKBiplane.Tests.csproj
```

Покрыты: нормализация угла, ограничение длины вектора, тяга и гравитация биплана,
урон пули, четыре попадания до уничтожения, полная зарядка копья, рывок, конечность
ввода бота, состав снимка и детерминизм симуляции.

## Docker

### Release

```powershell
Copy-Item .env.example .env
# заполните .env при необходимости
docker compose up -d --build
```

- клиент: `http://localhost:8081`
- сервер: `http://localhost:8080`
- health-check: `http://localhost:8080/health`

### Debug

```powershell
Copy-Item debug.env.example debug.env
# укажите параметры существующего PostgreSQL из shared-net
docker compose --env-file debug.env -f docker-compose.debug.yml up -d --build
```

- клиент: `http://localhost:5081`
- сервер: `http://localhost:5080`
- логи сервера: `MyaKBiplane.Server/Data/Logs/<год>/`

Debug-compose **не создаёт** новый PostgreSQL. Он подключается к внешней сети `shared-net`
и использует параметры подключения из `debug.env`.

## Секреты и конфигурация

- `.env` — release-окружение, в git не попадает.
- `debug.env` — локальное окружение, в git не попадает.
- `.env.example` и `debug.env.example` — шаблоны, которые коммитятся.
- Runtime-логи и локальные данные (`Data/`) игнорируются git.

## Логирование

Serilog настраивается через `LoggingProviders` и `LogFiles` в `appsettings*.json` по схеме
проекта SchoolShedule:

- `Console` — шаблон `[HH:mm:ss LVL] Message`;
- `File` — текстовый лог;
- `Monitoring` — JSON-лог.

Путь и срок хранения задаются в `LogFiles:Root` и `LogFiles:RetentionDays`.

## Дальше по плану

1. Playtest баланса и настройка бота.
2. Сетевой матч через SignalR: авторитетный сервер, интерполяция, реконнект.
3. Friend codes и приглашения в матч.
4. Android-клиент на MAUI Blazor Hybrid.
5. При нехватке FPS — нативный рендер SkiaSharp за тем же интерфейсом.

Архитектурные решения: [`docs/adr/0001-architecture.md`](docs/adr/0001-architecture.md).
