using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Client.Services;

/// <summary>
/// Сетевой матч, в котором игроки связаны напрямую через WebRTC DataChannel.
/// </summary>
/// <remarks>
/// SignalR нужен только на старте: сервер выдаёт код комнаты и ретранслирует SDP/ICE.
/// После открытия канала хост считает <see cref="HostGameSession"/> и шлёт снимки,
/// гость шлёт ввод. Сервер в самом матче не участвует.
/// </remarks>
public sealed class P2pMatchClient : IMatchClient
{
    private const int ConnectAttempts = 3;
    private const double SnapshotIntervalMs = 33d;
    private const double InputIntervalMs = 33d;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly NavigationManager _navigation;
    private readonly IConfiguration _configuration;
    private readonly IJSRuntime _js;

    private HubConnection? _connection;
    private DotNetObjectReference<P2pMatchClient>? _self;

    private string _code = string.Empty;
    private int _yourSlot;
    private bool _isHost;
    private PlaneKind _hostKind = PlaneKind.Biplane;
    private PlaneKind _guestKind = PlaneKind.Pterodactyl;
    private RoomSettings _settings = RoomSettings.Default;
    private RoomInfo _roomInfo = new();
    private string _status = "waiting";
    private int _pausedBySlot = -1;
    private int? _rematchOfferSlot;
    private string? _notice;

    private readonly List<string> _pendingSignals = new();
    private CancellationTokenSource? _peerTimeoutCts;

    private bool _peerStarted;
    private bool _peerConnected;
    private bool _signalsReady;
    private bool _offerCreated;
    private HostGameSession? _session;
    private string _latestSnapshot = string.Empty;

    private double _lastInputSent;
    private double _lastSnapshotSent;
    private double _lastTick;
    private bool _pendingReload;
    private bool _pendingEject;
    private bool _pendingParachute;
    private double _rttMilliseconds = -1;

    /// <summary>
    /// Создаёт P2P-клиент матча.
    /// </summary>
    /// <param name="navigation">Навигация Blazor.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="js">JavaScript-рантайм для WebRTC.</param>
    public P2pMatchClient(
        NavigationManager navigation,
        IConfiguration configuration,
        IJSRuntime js)
    {
        _navigation = navigation;
        _configuration = configuration;
        _js = js;
    }

    /// <inheritdoc />
    public event Action<RoomInfo>? RoomUpdated;

    /// <inheritdoc />
    public event Action<string>? SnapshotReceived;

    /// <inheritdoc />
    public event Action<string>? RoomClosed;

    /// <inheritdoc />
    public event Action<string>? ConnectionClosed;

    /// <inheritdoc />
    public Uri? HubUri => BuildHubUri();

    /// <inheritdoc />
    public Task ConnectAsync() =>
        _connection is { State: HubConnectionState.Connected }
            ? Task.CompletedTask
            : ConnectWithRetriesAsync();

    /// <inheritdoc />
    public async Task<RoomInfo> CreateRoomAsync(string kind, RoomSettings settings)
    {
        _isHost = true;
        _yourSlot = 0;
        _hostKind = ParseKind(kind);
        _guestKind = GameWorld.Opposite(_hostKind);
        _settings = settings.Normalized();

        var info = await InvokeHubAsync<RoomInfo>("CreateRoom", kind, _settings);
        ApplyServerInfo(info);
        return _roomInfo;
    }

    /// <inheritdoc />
    public async Task<RoomInfo> JoinRoomAsync(string code, string kind)
    {
        _isHost = false;
        _yourSlot = 1;
        _guestKind = ParseKind(kind);

        var info = await InvokeHubAsync<RoomInfo>("JoinRoom", code, kind);
        ApplyServerInfo(info);
        return _roomInfo;
    }

    /// <inheritdoc />
    public Task<RoomInfo> CreateBotRoomAsync(string kind) =>
        throw new NotSupportedException("P2P-матч не создаёт матчи с ботом.");

    /// <inheritdoc />
    public string Tick(InputState input, double timestamp)
    {
        if (!_peerConnected || _roomInfo.Status != "playing")
        {
            return _latestSnapshot;
        }

        return _isHost ? HostTick(input, timestamp) : GuestTick(input, timestamp);
    }

    /// <inheritdoc />
    public Task TogglePauseAsync()
    {
        if (_isHost && _session is not null)
        {
            if (_session.TogglePause(_yourSlot))
            {
                PublishSessionState();
            }

            return Task.CompletedTask;
        }

        return SendControlAsync("pause");
    }

    /// <inheritdoc />
    public Task OfferRematchAsync()
    {
        if (_isHost && _session is not null)
        {
            if (_session.OfferRematch(_yourSlot))
            {
                PublishSessionState();
            }

            return Task.CompletedTask;
        }

        return SendControlAsync("offer-rematch");
    }

    /// <inheritdoc />
    public Task RespondRematchAsync(bool accept)
    {
        if (_isHost && _session is not null)
        {
            if (_session.RespondRematch(_yourSlot, accept))
            {
                PublishSessionState();
            }

            return Task.CompletedTask;
        }

        return SendControlAsync("respond-rematch", accept);
    }

    /// <inheritdoc />
    public async Task LeaveRoomAsync()
    {
        ResetPeer();

        var connection = _connection;

        if (connection is not null && connection.State == HubConnectionState.Connected)
        {
            try
            {
                await connection.InvokeAsync("LeaveRoom");
            }
            catch (Exception)
            {
                // Соединение уже могло закрыться.
            }
        }
    }

    /// <inheritdoc />
    public Task<double> MeasureRttAsync()
    {
        if (!_peerConnected)
        {
            _rttMilliseconds = -1;
            return Task.FromResult(-1d);
        }

        SendPeer(new PeerMessage
        {
            Type = "ping",
            Timestamp = NowMilliseconds()
        });

        return Task.FromResult(_rttMilliseconds);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        ResetPeer();

        var connection = _connection;
        _connection = null;
        _self?.Dispose();
        _self = null;

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>Передаёт SDP/ICE через сервер.</summary>
    /// <param name="json">JSON сигнала.</param>
    [JSInvokable]
    public void OnWebRtcSignal(string json)
    {
        var connection = _connection;

        if (connection is not null && connection.State == HubConnectionState.Connected)
        {
            _ = SendHubSignalAsync(connection, json);
        }
    }

    /// <summary>Гость сообщил, что готов к соединению.</summary>
    [JSInvokable]
    public void OnPeerReady()
    {
        if (!_isHost || !_peerStarted || _offerCreated)
        {
            return;
        }

        _offerCreated = true;
        _ = CreateOfferAsync();
    }

    /// <summary>Вызывается при открытии прямого DataChannel.</summary>
    [JSInvokable]
    public void OnWebRtcConnected()
    {
        if (_peerConnected)
        {
            return;
        }

        _peerConnected = true;
        _peerTimeoutCts?.Cancel();

        if (_isHost)
        {
            _session = new HostGameSession(
                _hostKind,
                _guestKind,
                seed: HashCode.Combine(_code, Environment.TickCount),
                settings: _settings);
            _session.Start();

            _status = _session.Status;
            _roomInfo = _session.BuildInfo(_code, _yourSlot);

            var (hostSnapshot, guestSnapshot) = _session.CreateSnapshotPairJson();
            _latestSnapshot = hostSnapshot;
            SnapshotReceived?.Invoke(hostSnapshot);

            SendPeer(new PeerMessage
            {
                Type = "snapshot",
                Snapshot = guestSnapshot
            });
        }

        Publish();
    }

    /// <summary>Вызывается при сообщении по DataChannel.</summary>
    /// <param name="json">JSON сообщения.</param>
    [JSInvokable]
    public void OnWebRtcMessage(string json)
    {
        PeerMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<PeerMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "input" when _isHost && _session is not null && message.Input is not null:
                _session.SetInput(1, message.Input);
                break;

            case "snapshot" when !_isHost:
                _latestSnapshot = message.Snapshot ?? string.Empty;
                SnapshotReceived?.Invoke(_latestSnapshot);
                break;

            case "room" when !_isHost && message.Room is not null:
                ApplyRoomState(message.Room);
                break;

            case "control" when _isHost:
                HandleControl(message);
                break;

            case "ping":
                SendPeer(new PeerMessage
                {
                    Type = "pong",
                    Timestamp = message.Timestamp
                });
                break;

            case "pong":
                _rttMilliseconds = Math.Max(0d, NowMilliseconds() - message.Timestamp);
                break;
        }
    }

    /// <summary>Вызывается при разрыве прямого соединения.</summary>
    /// <param name="reason">Причина разрыва.</param>
    [JSInvokable]
    public void OnWebRtcDisconnected(string reason)
    {
        _peerConnected = false;
        ConnectionClosed?.Invoke(string.IsNullOrWhiteSpace(reason)
            ? "Прямое соединение с соперником потеряно."
            : reason);
    }

    private async Task ConnectWithRetriesAsync()
    {
        if (_connection is not null)
        {
            var old = _connection;
            _connection = null;
            await old.DisposeAsync();
        }

        Exception? lastError = null;

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            var connection = CreateConnection();

            try
            {
                var start = connection.StartAsync();

                if (await Task.WhenAny(start, Task.Delay(ConnectTimeout)) != start)
                {
                    throw new TimeoutException(
                        $"Сервер не ответил за {ConnectTimeout.TotalSeconds:0} с.");
                }

                await start;
                _connection = connection;
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
                await connection.DisposeAsync();

                if (attempt < ConnectAttempts)
                {
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
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
            })
            .Build();

        connection.On<RoomInfo>("RoomUpdated", ApplyServerInfo);
        connection.On<string>("Signal", json =>
        {
            lock (_pendingSignals)
            {
                if (!_signalsReady)
                {
                    // SDP/ICE может прийти раньше, чем WebRTC-модуль готов его принять.
                    _pendingSignals.Add(json);
                    return;
                }
            }

            _ = HandleSignalAsync(json);
        });
        connection.On<string>("RoomClosed", reason => RoomClosed?.Invoke(reason));
        connection.Closed += error =>
        {
            ConnectionClosed?.Invoke(error?.Message ?? "Соединение с сервером закрыто.");
            return Task.CompletedTask;
        };

        return connection;
    }

    private string HostTick(InputState input, double timestamp)
    {
        _pendingReload |= input.Reload;
        _pendingEject |= input.Eject;
        _pendingParachute |= input.Parachute;

        _session?.SetInput(0, new InputState
        {
            Thrust = input.Thrust,
            Turn = input.Turn,
            MoveX = input.MoveX,
            MoveY = input.MoveY,
            AimX = input.AimX,
            AimY = input.AimY,
            Fire = input.Fire,
            Ability = input.Ability,
            Reload = _pendingReload,
            Eject = _pendingEject,
            Parachute = _pendingParachute
        });

        _pendingReload = false;
        _pendingEject = false;
        _pendingParachute = false;

        if (_session is null)
        {
            return _latestSnapshot;
        }

        var delta = _lastTick <= 0d
            ? 0f
            : (float)Math.Min(0.25d, (timestamp - _lastTick) / 1000d);
        _lastTick = timestamp;

        _session.Update(delta);

        if (timestamp - _lastSnapshotSent >= SnapshotIntervalMs)
        {
            _lastSnapshotSent = timestamp;

            var (hostSnapshot, guestSnapshot) = _session.CreateSnapshotPairJson();
            _latestSnapshot = hostSnapshot;
            SnapshotReceived?.Invoke(hostSnapshot);
            SendPeer(new PeerMessage
            {
                Type = "snapshot",
                Snapshot = guestSnapshot
            });
        }

        if (_session.IsFinished && _roomInfo.Status != "finished")
        {
            PublishSessionState();
        }

        return _latestSnapshot;
    }

    private string GuestTick(InputState input, double timestamp)
    {
        _pendingReload |= input.Reload;
        _pendingEject |= input.Eject;
        _pendingParachute |= input.Parachute;

        var oneShot = _pendingReload || _pendingEject || _pendingParachute;

        if (oneShot || timestamp - _lastInputSent >= InputIntervalMs)
        {
            _lastInputSent = timestamp;

            SendPeer(new PeerMessage
            {
                Type = "input",
                Input = new InputState
                {
                    Thrust = input.Thrust,
                    Turn = input.Turn,
                    MoveX = input.MoveX,
                    MoveY = input.MoveY,
                    AimX = input.AimX,
                    AimY = input.AimY,
                    Fire = input.Fire,
                    Ability = input.Ability,
                    Reload = _pendingReload,
                    Eject = _pendingEject,
                    Parachute = _pendingParachute
                }
            });

            _pendingReload = false;
            _pendingEject = false;
            _pendingParachute = false;
        }

        return _latestSnapshot;
    }

    private void ApplyServerInfo(RoomInfo info)
    {
        _code = info.Code;
        _yourSlot = info.YourSlot;
        _isHost = info.IsHost;

        _hostKind = ParseKind(info.HostKind);
        _settings = info.Settings.Normalized();

        if (!string.IsNullOrWhiteSpace(info.GuestKind))
        {
            _guestKind = ParseKind(info.GuestKind);
        }

        ApplyRoomState(info with { PeerConnected = _peerConnected });

        if (info.OpponentConnected && !_peerStarted)
        {
            _ = StartPeerAsync();
        }
    }

    private void ApplyRoomState(RoomInfo info)
    {
        _status = info.Status;
        _pausedBySlot = info.PausedBySlot;
        _rematchOfferSlot = info.RematchOfferedByYou
            ? info.YourSlot
            : info.RematchOfferedByOpponent ? 1 - info.YourSlot : null;
        _notice = info.Notice;
        _roomInfo = info;

        Publish();
    }

    private void Publish()
    {
        _roomInfo = _roomInfo with
        {
            Status = _status,
            PausedBySlot = _pausedBySlot,
            RematchOfferedByYou = _rematchOfferSlot == _yourSlot,
            RematchOfferedByOpponent = _rematchOfferSlot is not null && _rematchOfferSlot != _yourSlot,
            Notice = _notice,
            PeerConnected = _peerConnected
        };

        RoomUpdated?.Invoke(_roomInfo);
    }

    private async Task StartPeerAsync()
    {
        _peerStarted = true;

        try
        {
            _self ??= DotNetObjectReference.Create(this);
            await _js.InvokeVoidAsync("myakWebrtc.start", _isHost, _self);

            _peerTimeoutCts?.Cancel();
            _peerTimeoutCts = new CancellationTokenSource();
            _ = WatchPeerTimeoutAsync(_peerTimeoutCts.Token);

            string[] queued;

            lock (_pendingSignals)
            {
                queued = _pendingSignals.ToArray();
                _pendingSignals.Clear();
                _signalsReady = true;
            }

            foreach (var json in queued)
            {
                await HandleSignalAsync(json);
            }

            if (!_isHost && _connection is not null)
            {
                // Хост создаёт offer только когда гость уже готов его принять.
                await SendHubSignalAsync(_connection, "{\"type\":\"ready\"}");
            }
        }
        catch (Exception exception)
        {
            ConnectionClosed?.Invoke(
                $"Не удалось установить прямое соединение: {exception.Message}");
        }
    }

    private async Task WatchPeerTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_peerConnected && !cancellationToken.IsCancellationRequested)
        {
            ConnectionClosed?.Invoke(
                "Не удалось установить прямое соединение за 20 секунд. " +
                "Возможно, сеть блокирует WebRTC — попробуйте другую сеть.");
        }
    }

    private async Task CreateOfferAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("myakWebrtc.createOffer");
        }
        catch (JSException exception)
        {
            ConnectionClosed?.Invoke(
                $"Не удалось установить прямое соединение: {exception.Message}");
        }
    }

    private void HandleControl(PeerMessage message)
    {
        if (_session is null)
        {
            return;
        }

        var changed = message.Action switch
        {
            "pause" => _session.TogglePause(message.Slot),
            "offer-rematch" => _session.OfferRematch(message.Slot),
            "respond-rematch" => _session.RespondRematch(message.Slot, message.Accept),
            _ => false
        };

        if (changed)
        {
            PublishSessionState();
        }
    }

    private void PublishSessionState()
    {
        if (_session is null)
        {
            return;
        }

        _status = _session.Status;
        _pausedBySlot = _session.PausedBySlot;
        _rematchOfferSlot = _session.RematchOfferSlot;
        _notice = _session.Notice;

        _roomInfo = _session.BuildInfo(_code, _yourSlot);
        Publish();

        SendPeer(new PeerMessage
        {
            Type = "room",
            Room = _session.BuildInfo(_code, 1 - _yourSlot)
        });
    }

    private Task SendControlAsync(string action, bool accept = false)
    {
        SendPeer(new PeerMessage
        {
            Type = "control",
            Action = action,
            Slot = _yourSlot,
            Accept = accept
        });

        return Task.CompletedTask;
    }

    private void SendPeer(PeerMessage message)
    {
        string json;

        try
        {
            json = JsonSerializer.Serialize(message, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        try
        {
            if (_js is IJSInProcessRuntime inProcess)
            {
                inProcess.InvokeVoid("myakWebrtc.send", json);
            }
            else
            {
                _ = SendPeerAsync(json);
            }
        }
        catch (JSException)
        {
            // Канал мог закрыться — следующий снимок покажет актуальное состояние.
        }
    }

    private void ResetPeer()
    {
        _peerTimeoutCts?.Cancel();
        _peerTimeoutCts?.Dispose();
        _peerTimeoutCts = null;

        _peerStarted = false;
        _peerConnected = false;
        _signalsReady = false;
        _offerCreated = false;

        lock (_pendingSignals)
        {
            _pendingSignals.Clear();
        }
        _session = null;
        _latestSnapshot = string.Empty;
        _lastTick = 0d;
        _lastSnapshotSent = 0d;
        _lastInputSent = 0d;

        try
        {
            _ = ClosePeerAsync();
        }
        catch (Exception)
        {
            // Страница уже выгружается.
        }
    }

    private async Task SendPeerAsync(string json)
    {
        try
        {
            await _js.InvokeVoidAsync("myakWebrtc.send", json);
        }
        catch (JSException)
        {
            // Канал мог закрыться.
        }
    }

    private async Task ClosePeerAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("myakWebrtc.close");
        }
        catch (JSException)
        {
            // Браузер уже выгружает страницу.
        }
    }

    private async Task HandleSignalAsync(string json)
    {
        try
        {
            await _js.InvokeVoidAsync("myakWebrtc.handleSignal", json);
        }
        catch (JSException)
        {
            // Браузер уже выгружает страницу.
        }
    }

    private static async Task SendHubSignalAsync(HubConnection connection, string json)
    {
        try
        {
            await connection.SendAsync("SendSignal", json);
        }
        catch (Exception)
        {
            // Сервер мог остановиться — игра уже идёт напрямую.
        }
    }

    private Task<T> InvokeHubAsync<T>(string method, params object?[] args)
    {
        var connection = _connection
            ?? throw new InvalidOperationException("Соединение с сервером не установлено.");

        return connection.InvokeCoreAsync<T>(method, args);
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

    private string DescribeConnectFailure(Exception? error)
    {
        var host = BuildHubUri().Host;
        var detail = error?.GetBaseException().Message;

        var text = $"Не удалось связаться с сервером сигналинга ({host}). " +
                   "Проверьте интернет: если включён VPN или блокировщик — выключите и повторите, " +
                   "если выключен — попробуйте включить. На мобильной сети сервер может быть недоступен " +
                   "из-за блокировки провайдера.";

        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} Техническая информация: {detail}";
    }

    private static PlaneKind ParseKind(string? kindName) =>
        Enum.TryParse<PlaneKind>(kindName, ignoreCase: true, out var kind)
            ? kind
            : PlaneKind.Biplane;

    private static double NowMilliseconds() =>
        new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
}
