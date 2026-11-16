using Microsoft.AspNetCore.SignalR;

namespace MyaKBiplane.Server.Hubs;

/// <summary>
/// Сигнальный хаб для будущих онлайн-матчей.
/// </summary>
/// <remarks>
/// На текущем этапе хаб служит проверкой инфраструктуры SignalR.
/// Игровые сессии и синхронизация состояния будут добавлены отдельным модулем.
/// </remarks>
public sealed class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;

    /// <summary>
    /// Создаёт хаб.
    /// </summary>
    /// <param name="logger">Логгер хаба.</param>
    public GameHub(ILogger<GameHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Проверяет доступность хаба.
    /// </summary>
    /// <returns>Строка <c>pong</c>.</returns>
    public Task<string> Ping() => Task.FromResult("pong");

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
        await base.OnDisconnectedAsync(exception);
    }
}
