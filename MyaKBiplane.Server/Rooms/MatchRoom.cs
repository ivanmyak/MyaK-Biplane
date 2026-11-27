using Microsoft.AspNetCore.SignalR;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Server.Hubs;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Server.Rooms;

/// <summary>
/// Реестр комнаты для P2P-матча: сервер помнит код, игроков и классы аппаратов,
/// а также ретранслирует SDP/ICE до открытия прямого канала. Сам матч считает хост.
/// </summary>
public sealed class MatchRoom : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<MatchRoom> _logger;
    private readonly RoomSettings _settings;

    private string _status = "waiting";
    private bool _closed;

    /// <summary>
    /// Создаёт комнату.
    /// </summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="hostConnectionId">Подключение хозяина.</param>
    /// <param name="hostKind">Класс аппарата хозяина.</param>
    /// <param name="settings">Правила комнаты.</param>
    /// <param name="hub">Контекст SignalR.</param>
    /// <param name="logger">Логгер комнаты.</param>
    public MatchRoom(
        string code,
        string hostConnectionId,
        PlaneKind hostKind,
        RoomSettings? settings,
        IHubContext<GameHub> hub,
        ILogger<MatchRoom> logger)
    {
        Code = code;
        HostConnectionId = hostConnectionId;
        HostKind = hostKind;
        GuestKind = GameWorld.Opposite(hostKind);
        _settings = (settings ?? RoomSettings.Default).Normalized();
        _hub = hub;
        _logger = logger;
    }

    /// <summary>Код комнаты.</summary>
    public string Code { get; }

    /// <summary>Подключение хозяина.</summary>
    public string HostConnectionId { get; }

    /// <summary>Подключение гостя, если он уже вошёл.</summary>
    public string? GuestConnectionId { get; private set; }

    /// <summary>Класс аппарата хозяина.</summary>
    public PlaneKind HostKind { get; }

    /// <summary>Класс аппарата гостя.</summary>
    public PlaneKind GuestKind { get; private set; }

    /// <summary>Правила комнаты.</summary>
    public RoomSettings Settings => _settings;

    /// <summary>Статус комнаты на стороне сервера.</summary>
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
    /// Подключает гостя с его классом аппарата.
    /// </summary>
    /// <param name="connectionId">Подключение гостя.</param>
    /// <param name="guestKind">Класс аппарата гостя.</param>
    /// <returns>Информация о комнате для гостя или <c>null</c>, если место занято.</returns>
    public RoomInfo? Join(string connectionId, PlaneKind guestKind)
    {
        lock (_sync)
        {
            if (_closed || GuestConnectionId is not null)
            {
                return null;
            }

            // Если хозяин запретил одинаковые аппараты, гостю достаётся
            // противоположный класс: так комната остаётся доступной,
            // а гость увидит пояснение в интерфейсе.
            GuestKind = _settings.ResolveGuestKind(HostKind, guestKind);
            GuestConnectionId = connectionId;
            _status = "playing";
            return BuildInfoLocked(1);
        }
    }

    /// <summary>
    /// Ретранслирует SDP/ICE от одного игрока другому.
    /// </summary>
    /// <param name="fromConnectionId">Отправитель.</param>
    /// <param name="payload">JSON с описанием или кандидатом.</param>
    public async Task RelaySignalAsync(string fromConnectionId, string payload)
    {
        var target = GetOtherConnectionId(fromConnectionId);

        if (target is null)
        {
            return;
        }

        try
        {
            await _hub.Clients.Client(target).SendAsync("Signal", payload);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Не удалось ретранслировать сигнал комнаты {Code}", Code);
        }
    }

    /// <summary>Возвращает подключение соперника.</summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <returns>Подключение соперника или <c>null</c>.</returns>
    public string? GetOtherConnectionId(string connectionId) =>
        connectionId == HostConnectionId ? GuestConnectionId : HostConnectionId;

    /// <summary>Возвращает слот подключения.</summary>
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

    /// <summary>Формирует состояние комнаты для игрока.</summary>
    /// <param name="slot">Слот получателя.</param>
    /// <returns>Информация о комнате.</returns>
    public RoomInfo BuildInfo(int slot)
    {
        lock (_sync)
        {
            return BuildInfoLocked(slot);
        }
    }

    /// <summary>Рассылает обновлённое состояние комнаты обоим игрокам.</summary>
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

    /// <summary>Закрывает комнату.</summary>
    public void Close()
    {
        lock (_sync)
        {
            _closed = true;
            _status = "closed";
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private RoomInfo BuildInfoLocked(int slot) => new()
    {
        Code = Code,
        Mode = "online",
        Status = _status,
        YourSlot = slot,
        IsHost = slot == 0,
        HostKind = SnapshotFactory.ToKindName(HostKind),
        GuestKind = GuestConnectionId is not null
            ? SnapshotFactory.ToKindName(GuestKind)
            : null,
        Settings = _settings,
        OpponentConnected = GuestConnectionId is not null
    };

    private IEnumerable<(string ConnectionId, int Slot)> EnumeratePlayers()
    {
        yield return (HostConnectionId, 0);

        if (GuestConnectionId is not null)
        {
            yield return (GuestConnectionId, 1);
        }
    }
}
