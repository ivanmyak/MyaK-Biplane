using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Server.Hubs;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Server.Rooms;

/// <summary>
/// Реестр активных P2P-комнат. Сервер выдаёт коды, помнит игроков и ретранслирует
/// сигналы WebRTC; расчёт матча целиком происходит в браузере хозяина комнаты.
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
    /// Создаёт комнату и ждёт второго игрока.
    /// </summary>
    /// <param name="connectionId">Подключение хозяина.</param>
    /// <param name="kindName">Класс аппарата хозяина.</param>
    /// <param name="settings">Правила комнаты.</param>
    /// <returns>Информация о комнате с кодом.</returns>
    public async Task<RoomInfo> CreateOnlineRoomAsync(
        string connectionId,
        string kindName,
        RoomSettings? settings = null)
    {
        await LeaveInternalAsync(connectionId, notifyOpponent: false);

        var kind = ParseKind(kindName);
        var code = CreateCode();
        var room = new MatchRoom(
            code,
            connectionId,
            kind,
            settings,
            _hub,
            _loggerFactory.CreateLogger<MatchRoom>());

        _roomsByCode[code] = room;
        _roomsByConnection[connectionId] = room;
        _logger.LogInformation(
            "Создана сетевая комната {Code}: подключение {ConnectionId}, класс {Kind}, " +
            "лимит {ScoreToWin}, одинаковые {AllowSameKind}, катапульта {AllowEjection}",
            code,
            connectionId,
            kind,
            room.Settings.ScoreToWin,
            room.Settings.AllowSameKind,
            room.Settings.AllowEjection);

        return room.BuildInfo(0);
    }

    /// <summary>
    /// Присоединяет игрока к комнате по коду.
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
            room.GuestKind);

        // Хост узнаёт о госте и начинает WebRTC-предложение.
        await room.NotifyRoomChangedAsync();
        return info;
    }

    /// <summary>
    /// Передаёт SDP/ICE сопернику по комнате игрока.
    /// </summary>
    /// <param name="connectionId">Отправитель.</param>
    /// <param name="payload">JSON сигнала.</param>
    public Task RelaySignalAsync(string connectionId, string payload)
    {
        if (_roomsByConnection.TryGetValue(connectionId, out var room))
        {
            return room.RelaySignalAsync(connectionId, payload);
        }

        return Task.CompletedTask;
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

    private async Task LeaveInternalAsync(string connectionId, bool notifyOpponent)
    {
        if (!_roomsByConnection.TryRemove(connectionId, out var room))
        {
            return;
        }

        var otherConnectionId = room.GetOtherConnectionId(connectionId);

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
