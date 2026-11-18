using Microsoft.AspNetCore.SignalR;
using MyaKBiplane.Server.Rooms;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Server.Hubs;

/// <summary>
/// Хаб сетевых матчей: создание комнат, ввод, пауза и реванш.
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
    /// Создаёт матч с ботом.
    /// </summary>
    /// <param name="kind">Класс аппарата игрока.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> CreateBotRoom(string kind) =>
        _rooms.CreateBotRoomAsync(Context.ConnectionId, kind);

    /// <summary>
    /// Создаёт сетевую комнату.
    /// </summary>
    /// <param name="kind">Класс аппарата хозяина.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> CreateRoom(string kind) =>
        _rooms.CreateOnlineRoomAsync(Context.ConnectionId, kind);

    /// <summary>
    /// Присоединяется к сетевой комнате по коду.
    /// </summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="kind">Класс аппарата гостя.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> JoinRoom(string code, string kind) =>
        _rooms.JoinRoomAsync(Context.ConnectionId, code, kind);

    /// <summary>
    /// Отправляет ввод игрока.
    /// </summary>
    /// <param name="input">Ввод.</param>
    public void SendInput(InputState input) => _rooms.SendInput(Context.ConnectionId, input);

    /// <summary>
    /// Переключает паузу.
    /// </summary>
    public Task TogglePause() => _rooms.TogglePauseAsync(Context.ConnectionId);

    /// <summary>
    /// Предлагает реванш.
    /// </summary>
    public Task OfferRematch() => _rooms.OfferRematchAsync(Context.ConnectionId);

    /// <summary>
    /// Отвечает на предложение реванша.
    /// </summary>
    /// <param name="accept">Принять ли реванш.</param>
    public Task RespondRematch(bool accept) =>
        _rooms.RespondRematchAsync(Context.ConnectionId, accept);

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
