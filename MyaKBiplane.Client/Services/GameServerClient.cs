using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Client.Services;

/// <summary>
/// Обёртка над SignalR-подключением к игровому серверу.
/// </summary>
public sealed class GameServerClient : IAsyncDisposable
{
    private readonly NavigationManager _navigation;
    private readonly IConfiguration _configuration;
    private HubConnection? _connection;
    private readonly object _rttLock = new();
    private double _rttMilliseconds = -1;

    /// <summary>
    /// Создаёт клиент сервера.
    /// </summary>
    /// <param name="navigation">Навигация Blazor для определения адреса.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    public GameServerClient(NavigationManager navigation, IConfiguration configuration)
    {
        _navigation = navigation;
        _configuration = configuration;
    }

    /// <summary>Событие обновления состояния комнаты.</summary>
    public event Action<RoomInfo>? RoomUpdated;

    /// <summary>Событие получения JSON-снимка состояния.</summary>
    public event Action<string>? SnapshotReceived;

    /// <summary>Событие закрытия комнаты соперником.</summary>
    public event Action<string>? RoomClosed;

    /// <summary>Событие разрыва соединения.</summary>
    public event Action<string>? ConnectionClosed;

    /// <summary>
    /// Последнее измеренное время туда-обратно в миллисекундах. -1 — нет данных.
    /// </summary>
    public double RttMilliseconds
    {
        get
        {
            lock (_rttLock)
            {
                return _rttMilliseconds;
            }
        }
    }

    /// <summary>
    /// Измеряет задержку до сервера через метод хаба Ping.
    /// </summary>
    public async Task<double> MeasureRttAsync()
    {
        var connection = _connection;

        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return -1;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await connection.InvokeAsync<string>("Ping");
            stopwatch.Stop();

            var rtt = stopwatch.Elapsed.TotalMilliseconds;

            lock (_rttLock)
            {
                _rttMilliseconds = rtt;
            }

            return rtt;
        }
        catch (Exception)
        {
            lock (_rttLock)
            {
                _rttMilliseconds = -1;
            }

            return -1;
        }
    }

    /// <summary>
    /// Устанавливает соединение с сервером.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(BuildHubUri(), options =>
            {
                // Только WebSocket. По умолчанию SignalR может тихо откатиться
                // на SSE/long polling — это добавляет сотни миллисекунд и делает
                // матч непригодным для живого боя. Если WS недоступен, лучше сразу показать ошибку.
                options.Transports = HttpTransportType.WebSockets;
            })
            .Build();

        _connection.On<RoomInfo>("RoomUpdated", info => RoomUpdated?.Invoke(info));
        _connection.On<string>("Snapshot", json => SnapshotReceived?.Invoke(json));
        _connection.On<string>("RoomClosed", reason => RoomClosed?.Invoke(reason));
        _connection.Closed += error =>
        {
            ConnectionClosed?.Invoke(error?.Message ?? "Соединение с сервером закрыто.");
            return Task.CompletedTask;
        };

        await _connection.StartAsync();
    }

    /// <summary>
    /// Создаёт матч с ботом.
    /// </summary>
    /// <param name="kind">Класс аппарата.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> CreateBotRoomAsync(string kind) =>
        InvokeAsync<RoomInfo>("CreateBotRoom", kind);

    /// <summary>
    /// Создаёт сетевую комнату.
    /// </summary>
    /// <param name="kind">Класс аппарата.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> CreateRoomAsync(string kind) =>
        InvokeAsync<RoomInfo>("CreateRoom", kind);

    /// <summary>
    /// Присоединяется к комнате по коду.
    /// </summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="kind">Класс аппарата.</param>
    /// <returns>Информация о комнате.</returns>
    public Task<RoomInfo> JoinRoomAsync(string code, string kind) =>
        InvokeAsync<RoomInfo>("JoinRoom", code, kind);

    /// <summary>
    /// Отправляет ввод игрока без ожидания ответа.
    /// </summary>
    /// <param name="input">Ввод.</param>
    public async Task SendInputAsync(InputState input)
    {
        var connection = _connection;

        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        await connection.SendAsync("SendInput", input);
    }

    /// <summary>
    /// Переключает паузу.
    /// </summary>
    public Task TogglePauseAsync() => InvokeAsync("TogglePause");

    /// <summary>
    /// Предлагает реванш.
    /// </summary>
    public Task OfferRematchAsync() => InvokeAsync("OfferRematch");

    /// <summary>
    /// Отвечает на предложение реванша.
    /// </summary>
    /// <param name="accept">Принять ли реванш.</param>
    public Task RespondRematchAsync(bool accept) => InvokeAsync("RespondRematch", accept);

    /// <summary>
    /// Выходит из комнаты.
    /// </summary>
    public async Task LeaveRoomAsync()
    {
        var connection = _connection;

        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await connection.InvokeAsync("LeaveRoom");
        }
        catch (Exception)
        {
            // Соединение уже могло закрыться.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
    }

    private Uri BuildHubUri()
    {
        var configured = _configuration["Server:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var serverBase = new Uri(configured.TrimEnd('/') + "/");
            return new Uri(serverBase, "hubs/game");
        }

        return new Uri(new Uri(_navigation.BaseUri), "hubs/game");
    }

    private async Task<T> InvokeAsync<T>(string method, params object?[] args)
    {
        var connection = _connection
            ?? throw new InvalidOperationException("Соединение с сервером не установлено.");

        return await connection.InvokeCoreAsync<T>(method, args);
    }

    private async Task InvokeAsync(string method, params object?[] args)
    {
        var connection = _connection
            ?? throw new InvalidOperationException("Соединение с сервером не установлено.");

        await connection.InvokeCoreAsync(method, args);
    }
}
