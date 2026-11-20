using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Server.Hubs;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Server.Rooms;

/// <summary>
/// Хранит активные комнаты и связывает их с подключениями SignalR.
/// </summary>
public sealed class RoomManager
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly ConcurrentDictionary<string, MatchRoom> _roomsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MatchRoom> _roomsByConnection = new(StringComparer.Ordinal);
    private readonly IHubContext<GameHub> _hub;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RoomManager> _logger;

    /// <summary>
    /// Создаёт менеджер комнат.
    /// </summary>
    /// <param name="hub">Контекст SignalR.</param>
    /// <param name="loggerFactory">Фабрика логгеров.</param>
    public RoomManager(IHubContext<GameHub> hub, ILoggerFactory loggerFactory)
    {
        _hub = hub;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RoomManager>();
    }

    /// <summary>
    /// Создаёт матч с ботом и сразу запускает его.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <param name="kindName">Класс аппарата игрока.</param>
    /// <returns>Информация о комнате.</returns>
    public async Task<RoomInfo> CreateBotRoomAsync(string connectionId, string kindName)
    {
        await LeaveInternalAsync(connectionId, notifyOpponent: false);

        var kind = ParseKind(kindName);
        var room = CreateRoom(
            code: string.Empty,
            mode: "bot",
            hostConnectionId: connectionId,
            hostKind: kind,
            guestConnectionId: null,
            guestKind: GameWorld.Opposite(kind));

        _roomsByConnection[connectionId] = room;
        _logger.LogInformation(
            "Создан матч с ботом: подключение {ConnectionId}, класс {Kind}",
            connectionId,
            kind);

        return room.BuildInfo(0);
    }

    /// <summary>
    /// Создаёт сетевую комнату и ждёт второго игрока.
    /// </summary>
    /// <param name="connectionId">Подключение хозяина.</param>
    /// <param name="kindName">Класс аппарата хозяина.</param>
    /// <returns>Информация о комнате с кодом.</returns>
    public async Task<RoomInfo> CreateOnlineRoomAsync(string connectionId, string kindName)
    {
        await LeaveInternalAsync(connectionId, notifyOpponent: false);

        var kind = ParseKind(kindName);
        var code = CreateCode();
        var room = CreateRoom(
            code,
            mode: "online",
            hostConnectionId: connectionId,
            hostKind: kind,
            guestConnectionId: null,
            guestKind: GameWorld.Opposite(kind));

        _roomsByCode[code] = room;
        _roomsByConnection[connectionId] = room;
        _logger.LogInformation(
            "Создана сетевая комната {Code}: подключение {ConnectionId}, класс {Kind}",
            code,
            connectionId,
            kind);

        return room.BuildInfo(0);
    }

    /// <summary>
    /// Присоединяет игрока к сетевой комнате.
    /// </summary>
    /// <param name="connectionId">Подключение гостя.</param>
    /// <param name="code">Код комнаты.</param>
    /// <param name="kindName">Класс аппарата гостя.</param>
    /// <returns>Информация о комнате.</returns>
    public async Task<RoomInfo> JoinRoomAsync(string connectionId, string code, string kindName)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (!_roomsByCode.TryGetValue(normalizedCode, out var room))
        {
            throw new HubException("Комната не найдена. Проверьте код.");
        }

        await LeaveInternalAsync(connectionId, notifyOpponent: true);

        var kind = ParseKind(kindName);
        var info = room.Join(connectionId, kind);

        if (info is null)
        {
            throw new HubException("В комнате уже есть второй игрок.");
        }

        _roomsByConnection[connectionId] = room;
        _logger.LogInformation(
            "Игрок {ConnectionId} присоединился к комнате {Code} с классом {Kind}",
            connectionId,
            normalizedCode,
            kind);

        await room.NotifyRoomChangedAsync();
        return info;
    }

    /// <summary>
    /// Сохраняет ввод игрока.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <param name="input">Ввод.</param>
    public void SendInput(string connectionId, InputState input)
    {
        if (!_roomsByConnection.TryGetValue(connectionId, out var room))
        {
            return;
        }

        if (room.GetSlot(connectionId) is null)
        {
            return;
        }

        room.SetInput(connectionId, input);
    }

    /// <summary>
    /// Переключает паузу в комнате игрока.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    public async Task TogglePauseAsync(string connectionId)
    {
        if (!_roomsByConnection.TryGetValue(connectionId, out var room))
        {
            return;
        }

        if (room.TogglePause(connectionId))
        {
            await room.NotifyRoomChangedAsync();
        }
    }

    /// <summary>
    /// Предлагает реванш.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    public async Task OfferRematchAsync(string connectionId)
    {
        if (!_roomsByConnection.TryGetValue(connectionId, out var room))
        {
            return;
        }

        if (room.OfferRematch(connectionId))
        {
            await room.NotifyRoomChangedAsync();
        }
    }

    /// <summary>
    /// Отвечает на предложение реванша.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    /// <param name="accept">Принять ли реванш.</param>
    public async Task RespondRematchAsync(string connectionId, bool accept)
    {
        if (!_roomsByConnection.TryGetValue(connectionId, out var room))
        {
            return;
        }

        if (room.RespondRematch(connectionId, accept))
        {
            await room.NotifyRoomChangedAsync();
        }
    }

    /// <summary>
    /// Выводит игрока из комнаты.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    public Task LeaveAsync(string connectionId) =>
        LeaveInternalAsync(connectionId, notifyOpponent: true);

    /// <summary>
    /// Обрабатывает отключение игрока.
    /// </summary>
    /// <param name="connectionId">Подключение игрока.</param>
    public Task DisconnectAsync(string connectionId) =>
        LeaveInternalAsync(connectionId, notifyOpponent: true);

    private MatchRoom CreateRoom(
        string code,
        string mode,
        string hostConnectionId,
        PlaneKind hostKind,
        string? guestConnectionId,
        PlaneKind guestKind)
    {
        return new MatchRoom(
            code,
            mode,
            hostConnectionId,
            hostKind,
            guestConnectionId,
            guestKind,
            _hub,
            _loggerFactory.CreateLogger<MatchRoom>());
    }

    private async Task LeaveInternalAsync(string connectionId, bool notifyOpponent)
    {
        if (!_roomsByConnection.TryRemove(connectionId, out var room))
        {
            return;
        }

        var otherConnectionId = room.HostConnectionId == connectionId
            ? room.GuestConnectionId
            : room.HostConnectionId;

        if (otherConnectionId is not null)
        {
            _roomsByConnection.TryRemove(otherConnectionId, out _);

            if (notifyOpponent)
            {
                try
                {
                    await _hub.Clients.Client(otherConnectionId)
                        .SendAsync("RoomClosed", "Соперник покинул игру");
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Не удалось уведомить соперника о закрытии комнаты");
                }
            }
        }

        if (!string.IsNullOrEmpty(room.Code))
        {
            _roomsByCode.TryRemove(room.Code, out _);
        }

        room.Close();
        await room.DisposeAsync();
        _logger.LogInformation("Комната {Code} закрыта", room.Code);
    }

    private string CreateCode()
    {
        var buffer = new char[CodeLength];

        for (var attempt = 0; attempt < 100; attempt++)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
            }

            var code = new string(buffer);

            if (!_roomsByCode.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Не удалось создать уникальный код комнаты.");
    }

    private static PlaneKind ParseKind(string kindName)
    {
        if (Enum.TryParse<PlaneKind>(kindName, ignoreCase: true, out var kind))
        {
            return kind;
        }

        throw new HubException("Неизвестный класс летательного аппарата.");
    }
}
