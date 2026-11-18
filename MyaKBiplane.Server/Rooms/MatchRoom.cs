using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Server.Hubs;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Server.Rooms;

/// <summary>
/// Одна комната матча: серверный шаг симуляции, пауза, реванш и рассылка снимков.
/// </summary>
public sealed class MatchRoom : IAsyncDisposable
{
    private const float FixedStep = 1f / 60f;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(33);

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<MatchRoom> _logger;
    private readonly GameWorld _world;
    private readonly BotController? _bot;
    private readonly InputState _input0 = new();
    private readonly InputState _input1 = new();
    private readonly List<GameEvent> _events = new(128);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private string _status = "waiting";
    private int _pausedBySlot = -1;
    private int? _rematchOfferSlot;
    private string? _notice;
    private int? _noticeSlot;
    private bool _closed;

    /// <summary>
    /// Создаёт комнату и запускает серверный цикл.
    /// </summary>
    /// <param name="code">Код комнаты или пустая строка для матча с ботом.</param>
    /// <param name="mode">Режим <c>bot</c> или <c>online</c>.</param>
    /// <param name="hostConnectionId">Подключение хозяина комнаты.</param>
    /// <param name="hostKind">Класс аппарата хозяина.</param>
    /// <param name="guestConnectionId">Подключение гостя, если оно уже известно.</param>
    /// <param name="guestKind">Класс аппарата гостя.</param>
    /// <param name="hub">Контекст SignalR.</param>
    /// <param name="logger">Логгер комнаты.</param>
    public MatchRoom(
        string code,
        string mode,
        string hostConnectionId,
        PlaneKind hostKind,
        string? guestConnectionId,
        PlaneKind guestKind,
        IHubContext<GameHub> hub,
        ILogger<MatchRoom> logger)
    {
        Code = code;
        Mode = mode;
        HostConnectionId = hostConnectionId;
        GuestConnectionId = guestConnectionId;
        HostKind = hostKind;
        GuestKind = guestKind;
        _hub = hub;
        _logger = logger;

        var seed = HashCode.Combine(code, hostConnectionId, Environment.TickCount);
        _world = new GameWorld(seed: seed);
        _bot = mode == "bot" ? new BotController(seed + 1, _world.Balance) : null;

        if (mode == "bot")
        {
            StartMatchLocked();
        }

        _loop = Task.Run(LoopAsync);
    }

    /// <summary>Код комнаты.</summary>
    public string Code { get; }

    /// <summary>Режим комнаты.</summary>
    public string Mode { get; }

    /// <summary>Подключение хозяина.</summary>
    public string HostConnectionId { get; }

    /// <summary>Подключение гостя.</summary>
    public string? GuestConnectionId { get; private set; }

    /// <summary>Класс аппарата хозяина.</summary>
    public PlaneKind HostKind { get; }

    /// <summary>Класс аппарата гостя.</summary>
    public PlaneKind GuestKind { get; private set; }

    /// <summary>Статус комнаты.</summary>
    public string Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    /// <summary>Закрыта ли комната.</summary>
    public bool IsClosed
    {
        get
        {
            lock (_sync)
            {
                return _closed;
            }
        }
    }

    /// <summary>
    /// Подключает гостя и запускает матч.
    /// </summary>
    /// <param name="connectionId">Подключение гостя.</param>
    /// <param name="guestKind">Класс аппарата гостя.</param>
    /// <returns>Информация о комнате для гостя или <c>null</c>, если место занято.</returns>
    public RoomInfo? Join(string connectionId, PlaneKind guestKind)
    {
        lock (_sync)
        {
            if (_closed || Mode != "online" || GuestConnectionId is not null)
            {
                return null;
            }

            GuestConnectionId = connectionId;
            GuestKind = guestKind;
            StartMatchLocked();
            return BuildInfoLocked(1);
        }
    }

    /// <summary>
    /// Сохраняет последний ввод игрока.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <param name="input">Ввод.</param>
    public void SetInput(string connectionId, InputState input)
    {
        var target = connectionId == HostConnectionId ? _input0 : _input1;

        lock (_sync)
        {
            target.Thrust = input.Thrust;
            target.Turn = input.Turn;
            target.MoveX = input.MoveX;
            target.MoveY = input.MoveY;
            target.AimX = input.AimX;
            target.AimY = input.AimY;
            target.Fire = input.Fire;
            target.Ability = input.Ability;
            target.Reload = input.Reload;
            target.Eject = input.Eject;
            target.Parachute = input.Parachute;
        }
    }

    /// <summary>
    /// Ставит игру на паузу или снимает её.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <returns><c>true</c>, если статус изменился.</returns>
    public bool TogglePause(string connectionId)
    {
        var slot = GetSlot(connectionId);

        lock (_sync)
        {
            if (slot is null || _closed)
            {
                return false;
            }

            if (_status == "playing")
            {
                _status = "paused";
                _pausedBySlot = slot.Value;
                return true;
            }

            if (_status == "paused")
            {
                _status = "playing";
                _pausedBySlot = -1;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Предлагает реванш. В матче с ботом реванш принимается автоматически.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <returns><c>true</c>, если состояние комнаты изменилось.</returns>
    public bool OfferRematch(string connectionId)
    {
        var slot = GetSlot(connectionId);

        lock (_sync)
        {
            if (slot is null || _closed || _status == "waiting")
            {
                return false;
            }

            if (Mode == "bot")
            {
                StartMatchLocked();
                return true;
            }

            if (_rematchOfferSlot is not null && _rematchOfferSlot != slot)
            {
                StartMatchLocked();
                return true;
            }

            _rematchOfferSlot = slot;
            _notice = null;
            _noticeSlot = null;
            return true;
        }
    }

    /// <summary>
    /// Отвечает на предложение реванша.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <param name="accept">Принять ли реванш.</param>
    /// <returns><c>true</c>, если состояние комнаты изменилось.</returns>
    public bool RespondRematch(string connectionId, bool accept)
    {
        var slot = GetSlot(connectionId);

        lock (_sync)
        {
            if (slot is null
                || _closed
                || _rematchOfferSlot is null
                || _rematchOfferSlot == slot)
            {
                return false;
            }

            if (accept)
            {
                StartMatchLocked();
            }
            else
            {
                _notice = "Соперник отклонил реванш";
                _noticeSlot = _rematchOfferSlot;
                _rematchOfferSlot = null;
            }

            return true;
        }
    }

    /// <summary>
    /// Возвращает слот подключения.
    /// </summary>
    /// <param name="connectionId">Подключение.</param>
    /// <returns>Слот 0 или 1, либо <c>null</c>.</returns>
    public int? GetSlot(string connectionId)
    {
        if (connectionId == HostConnectionId)
        {
            return 0;
        }

        return GuestConnectionId is not null && connectionId == GuestConnectionId ? 1 : null;
    }

    /// <summary>
    /// Возвращает информацию о комнате для указанного слота.
    /// </summary>
    /// <param name="slot">Слот игрока.</param>
    /// <returns>Информация для клиента.</returns>
    public RoomInfo BuildInfo(int slot)
    {
        lock (_sync)
        {
            return BuildInfoLocked(slot);
        }
    }

    /// <summary>
    /// Рассылает обновлённое состояние комнаты игрокам.
    /// </summary>
    public async Task NotifyRoomChangedAsync()
    {
        foreach (var (connectionId, slot) in EnumeratePlayers())
        {
            try
            {
                await _hub.Clients.Client(connectionId).SendAsync("RoomUpdated", BuildInfo(slot));
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Не удалось отправить состояние комнаты {Code}", Code);
            }
        }
    }

    /// <summary>
    /// Закрывает комнату и останавливает цикл.
    /// </summary>
    public void Close()
    {
        lock (_sync)
        {
            _closed = true;
            _status = "closed";
        }

        _cts.Cancel();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Close();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Ожидаемое завершение цикла.
        }

        _cts.Dispose();
    }

    private void StartMatchLocked()
    {
        _world.Reset(HostKind, GuestKind, secondIsBot: Mode == "bot");
        _bot?.Reset();
        _events.Clear();
        _input0.Reload = false;
        _input0.Eject = false;
        _input0.Parachute = false;
        _input1.Reload = false;
        _input1.Eject = false;
        _input1.Parachute = false;
        _status = "playing";
        _pausedBySlot = -1;
        _rematchOfferSlot = null;
        _notice = null;
        _noticeSlot = null;
    }

    private RoomInfo BuildInfoLocked(int slot) => new()
    {
        Code = Code,
        Mode = Mode,
        Status = _status,
        YourSlot = slot,
        IsHost = slot == 0,
        HostKind = SnapshotFactory.ToKindName(HostKind),
        GuestKind = GuestConnectionId is not null || Mode == "bot"
            ? SnapshotFactory.ToKindName(GuestKind)
            : null,
        OpponentConnected = Mode == "bot" || GuestConnectionId is not null,
        PausedBySlot = _pausedBySlot,
        RematchOfferedByYou = _rematchOfferSlot == slot,
        RematchOfferedByOpponent = _rematchOfferSlot is not null && _rematchOfferSlot != slot,
        Notice = _noticeSlot is null || _noticeSlot == slot ? _notice : null
    };

    private IEnumerable<(string ConnectionId, int Slot)> EnumeratePlayers()
    {
        yield return (HostConnectionId, 0);

        if (GuestConnectionId is not null)
        {
            yield return (GuestConnectionId, 1);
        }
    }

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(FixedStep));
        var broadcastCounter = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                var broadcast = false;
                var roomChanged = false;

                lock (_sync)
                {
                    if (_closed)
                    {
                        return;
                    }

                    if (_status == "playing")
                    {
                        var botInput = _bot?.Update(_world, FixedStep) ?? default;
                        var secondInput = Mode == "bot" ? botInput : ToPlayerInput(_input1);
                        _world.Step(ToPlayerInput(_input0), secondInput, FixedStep);

                        // Разовые команды действуют ровно один шаг: иначе зажатый R
                        // запускал бы перезарядку сразу после первого же выстрела.
                        _input0.Reload = false;
                        _input0.Eject = false;
                        _input0.Parachute = false;
                        _input1.Reload = false;
                        _input1.Eject = false;
                        _input1.Parachute = false;

                        foreach (var gameEvent in _world.Events)
                        {
                            _events.Add(gameEvent);
                        }

                        if (_events.Count > 512)
                        {
                            _events.RemoveRange(0, _events.Count - 512);
                        }

                        if (_world.Status == MatchStatus.Finished)
                        {
                            _status = "finished";
                            _pausedBySlot = -1;
                            roomChanged = true;
                        }
                    }

                    broadcastCounter++;

                    if (broadcastCounter >= 2)
                    {
                        broadcastCounter = 0;
                        broadcast = true;
                    }
                }

                if (roomChanged)
                {
                    await NotifyRoomChangedAsync();
                }

                if (broadcast)
                {
                    await BroadcastSnapshotsAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Комната закрыта.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Цикл комнаты {Code} завершился ошибкой", Code);
        }
    }

    private async Task BroadcastSnapshotsAsync()
    {
        GameSnapshot? hostSnapshot = null;
        GameSnapshot? guestSnapshot = null;

        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            // Пока комната ждёт соперника, снимки не нужны.
            if (_status == "waiting")
            {
                return;
            }

            hostSnapshot = SnapshotFactory.Create(_world, 0, _status, _events);
            guestSnapshot = GuestConnectionId is not null
                ? SnapshotFactory.Create(_world, 1, _status, _events)
                : null;
            _events.Clear();
        }

        await SendSnapshotAsync(HostConnectionId, hostSnapshot);

        if (GuestConnectionId is not null && guestSnapshot is not null)
        {
            await SendSnapshotAsync(GuestConnectionId, guestSnapshot);
        }
    }

    private async Task SendSnapshotAsync(string connectionId, GameSnapshot snapshot)
    {
        try
        {
            var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
            await _hub.Clients.Client(connectionId).SendAsync("Snapshot", json);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Не удалось отправить снимок комнаты {Code}", Code);
        }
    }

    private static PlayerInput ToPlayerInput(InputState input) => new()
    {
        Thrust = input.Thrust,
        Turn = input.Turn,
        MoveX = input.MoveX,
        MoveY = input.MoveY,
        AimX = input.AimX,
        AimY = input.AimY,
        Fire = input.Fire,
        Ability = input.Ability,
        Reload = input.Reload,
        Eject = input.Eject,
        Parachute = input.Parachute
    };
}
