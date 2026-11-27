using Microsoft.AspNetCore.SignalR;
using MyaKBiplane.Server.Rooms;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Server.Hubs;

/// <summary>
/// Легкий хаб сигналинга: создаёт комнаты с кодом и передаёт SDP/ICE между игроками.
/// Ввод, симуляция, снимки, пауза и реванш идут напрямую между браузерами.
/// </summary>
public sealed class GameHub : Hub
{
    private readonly RoomManager _rooms;
    private readonly ILogger<GameHub> _logger;

    /// <summary>
    /// Создаёт хаб.
    /// </summary>
    /// <param name="rooms">Менеджер комнат.</param>
    /// <param name="logger">Логгер хаба.</param>
    public GameHub(RoomManager rooms, ILogger<GameHub> logger)
    {
        _rooms = rooms;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет доступность хаба.
    /// </summary>
    /// <returns>Строка <c>pong</c>.</returns>
    public Task<string> Ping() => Task.FromResult("pong");

    /// <summary>
    /// Создаёт сетевую комнату.
    /// </summary>
    /// <param name="kind">Класс аппарата хозяина.</param>
    /// <param name="settings">Правила комнаты. Пустые значения означают стандартные.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> CreateRoom(string kind, RoomSettings? settings = null) =>
        _rooms.CreateOnlineRoomAsync(Context.ConnectionId, kind, settings);

    /// <summary>
    /// Присоединяется к сетевой комнате по коду.
    /// </summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="kind">Класс аппарата гостя.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> JoinRoom(string code, string kind) =>
        _rooms.JoinRoomAsync(Context.ConnectionId, code, kind);

    /// <summary>
    /// Передаёт SDP/ICE сопернику.
    /// </summary>
    /// <param name="payload">JSON сигнала WebRTC.</param>
    public Task SendSignal(string payload) =>
        _rooms.RelaySignalAsync(Context.ConnectionId, payload);

    /// <summary>
    /// Выходит из комнаты.
    /// </summary>
    public Task LeaveRoom() => _rooms.LeaveAsync(Context.ConnectionId);

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Клиент подключился: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            exception,
            "Клиент отключился: {ConnectionId}",
            Context.ConnectionId);

        await _rooms.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
