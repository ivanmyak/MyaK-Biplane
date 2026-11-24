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
    /// <summary>Сколько раз пробовать подключиться, прежде чем показать ошибку.</summary>
    private const int ConnectAttempts = 3;

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

    /// <summary>Адрес хаба, используется для диагностики чистого WebSocket-RTT.</summary>
    public Uri HubUri => BuildHubUri();

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
    /// Устанавливает соединение с сервером. Делает несколько попыток: на мобильных
    /// сетях первый запрос часто обрывается (переключение Wi-Fi/LTE, холодный старт сервера).
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_connection is { State: HubConnectionState.Connected })
        {
            return;
        }

        if (_connection is not null)
        {
            await DisposeConnectionAsync();
        }

        Exception? lastError = null;

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            var connection = CreateConnection();

            try
            {
                await connection.StartAsync();
                _connection = connection;
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
                await connection.DisposeAsync();

                if (attempt < ConnectAttempts)
                {
                    // Короткая пауза перед повтором: даёт сети и машине на Fly время подняться.
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                }
            }
        }

        throw new InvalidOperationException(DescribeConnectFailure(lastError), lastError);
    }

    private HubConnection CreateConnection()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(BuildHubUri(), options =>
            {
                // Сразу WebSocket, без HTTP-negotiate: на один запрос меньше,
                // нет CORS-preflight и работает даже там, где прокси ломает POST.
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
            })
            .Build();

        connection.On<RoomInfo>("RoomUpdated", info => RoomUpdated?.Invoke(info));
        connection.On<string>("Snapshot", json => SnapshotReceived?.Invoke(json));
        connection.On<string>("RoomClosed", reason => RoomClosed?.Invoke(reason));
        connection.Closed += error =>
        {
            ConnectionClosed?.Invoke(error?.Message ?? "Соединение с сервером закрыто.");
            return Task.CompletedTask;
        };

        return connection;
    }

    /// <summary>
    /// Объясняет ошибку подключения человеческим языком, сохраняя техническую деталь.
    /// </summary>
    private string DescribeConnectFailure(Exception? error)
    {
        var host = BuildHubUri().Host;
        var detail = error?.GetBaseException().Message;

        var text = $"Не удалось связаться с сервером игры ({host}). " +
                   "Проверьте интернет: если включён VPN или блокировщик — выключите и повторите, " +
                   "если выключен — попробуйте включить. На мобильной сети сервер может быть недоступен " +
                   "из-за блокировки провайдера.";

        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} Техническая информация: {detail}";
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
    public async ValueTask DisposeAsync() => await DisposeConnectionAsync();

    private async ValueTask DisposeConnectionAsync()
    {
        var connection = _connection;
        _connection = null;

        if (connection is null)
        {
            return;
        }

        await connection.DisposeAsync();
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
