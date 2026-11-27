using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Client.Services;

/// <summary>
/// Сетевой матч напрямую между браузерами без сервера сигналинга.
/// </summary>
/// <remarks>
/// Хост создаёт код с SDP-предложением, гость вставляет его и возвращает код ответа.
/// После этого открывается DataChannel: хост считает <see cref="HostGameSession"/>
/// и шлёт снимки, гость шлёт ввод. GitHub Pages — единственная инфраструктура.
/// </remarks>
public sealed class P2pMatchClient : IManualMatchClient
{
    private const string CodePrefix = "MYAK1:";
    private const double SnapshotIntervalMs = 33d;
    private const double InputIntervalMs = 33d;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IJSRuntime _js;
    private readonly string _version;

    private DotNetObjectReference<P2pMatchClient>? _self;

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
    private string? _outgoingCode;
    private bool _kindAdjusted;

    private bool _peerStarted;
    private bool _peerConnected;
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
    /// Создаёт P2P-клиент.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения (версия для проверки кодов).</param>
    /// <param name="js">JavaScript-рантайм для WebRTC.</param>
    public P2pMatchClient(IConfiguration configuration, IJSRuntime js)
    {
        _js = js;
        _version = configuration["App:Version"] ?? "0.0.0";
    }

    /// <inheritdoc />
    public event Action<RoomInfo>? RoomUpdated;

    /// <inheritdoc />
    public event Action<string>? SnapshotReceived;

    /// <inheritdoc />
#pragma warning disable CS0067 // без сервера некому закрывать комнату
    public event Action<string>? RoomClosed;
#pragma warning restore CS0067

    /// <inheritdoc />
    public event Action<string>? ConnectionClosed;

    /// <inheritdoc />
    public Uri? HubUri => null;

    /// <inheritdoc />
    public string? OutgoingCode => _outgoingCode;

    /// <summary>Был ли класс гостя автоматически изменён из-за правила «разные аппараты».</summary>
    public bool KindAdjusted => _kindAdjusted;

    /// <inheritdoc />
    public Task ConnectAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<RoomInfo> CreateRoomAsync(string kind, RoomSettings settings)
    {
        _isHost = true;
        _yourSlot = 0;
        _hostKind = ParseKind(kind);
        _guestKind = GameWorld.Opposite(_hostKind);
        _settings = settings.Normalized();
        _status = "waiting";

        await StartPeerAsync();

        var rtc = await _js.InvokeAsync<string>("myakWebrtc.createOfferCode");
        _outgoingCode = Encode(new InviteCode
        {
            Version = _version,
            HostKind = KindName(_hostKind),
            Settings = _settings,
            Rtc = rtc
        });

        _roomInfo = BuildInfo();
        Publish();
        return _roomInfo;
    }

    /// <inheritdoc />
    public async Task<RoomInfo> JoinRoomAsync(string code, string kind)
    {
        var invite = Decode(code);

        _isHost = false;
        _yourSlot = 1;
        _hostKind = ParseKind(invite.HostKind);
        _settings = (invite.Settings ?? RoomSettings.Default).Normalized();

        var requestedKind = ParseKind(kind);
        _guestKind = _settings.ResolveGuestKind(_hostKind, requestedKind);
        _kindAdjusted = _guestKind != requestedKind;

        await StartPeerAsync();

        var rtc = await _js.InvokeAsync<string>("myakWebrtc.createAnswerCode", invite.Rtc);
        _outgoingCode = Encode(new InviteCode
        {
            Version = _version,
            HostKind = KindName(_hostKind),
            GuestKind = KindName(_guestKind),
            Settings = _settings,
            Rtc = rtc
        });

        // Гость готов к бою: как только DataChannel откроется, начнём слать ввод.
        _status = "playing";
        _roomInfo = BuildInfo();
        Publish();
        return _roomInfo;
    }

    /// <inheritdoc />
    public async Task AcceptCodeAsync(string code)
    {
        if (!_isHost)
        {
            throw new InvalidOperationException("Код ответа вводит хозяин комнаты.");
        }

        var answer = Decode(code);

        if (!string.IsNullOrWhiteSpace(answer.GuestKind))
        {
            _guestKind = ParseKind(answer.GuestKind);
        }

        if (answer.Settings is not null && !SameSettings(answer.Settings, _settings))
        {
            throw new InvalidOperationException("Соперник прислал код с другими правилами комнаты.");
        }

        await _js.InvokeVoidAsync("myakWebrtc.acceptAnswerCode", answer.Rtc);
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
    public Task LeaveRoomAsync()
    {
        ResetPeer();
        return Task.CompletedTask;
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

        var self = _self;
        _self = null;
        self?.Dispose();

        await ValueTask.CompletedTask;
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

        if (_isHost)
        {
            _session = new HostGameSession(
                _hostKind,
                _guestKind,
                seed: HashCode.Combine(Environment.TickCount, (int)_hostKind),
                settings: _settings);
            _session.Start();

            _status = _session.Status;
            _roomInfo = _session.BuildInfo(string.Empty, _yourSlot);

            var (hostSnapshot, guestSnapshot) = _session.CreateSnapshotPairJson();
            _latestSnapshot = hostSnapshot;
            SnapshotReceived?.Invoke(hostSnapshot);

            SendPeer(new PeerMessage
            {
                Type = "snapshot",
                Snapshot = guestSnapshot
            });
            SendPeer(new PeerMessage
            {
                Type = "room",
                Room = _session.BuildInfo(string.Empty, 1 - _yourSlot)
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

    private async Task StartPeerAsync()
    {
        if (_peerStarted)
        {
            return;
        }

        _peerStarted = true;
        _self ??= DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("myakWebrtc.start", _isHost, _self);
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

    private void ApplyRoomState(RoomInfo info)
    {
        _status = info.Status;
        _pausedBySlot = info.PausedBySlot;
        _rematchOfferSlot = info.RematchOfferedByYou
            ? info.YourSlot
            : info.RematchOfferedByOpponent ? 1 - info.YourSlot : null;
        _notice = info.Notice;
        _settings = info.Settings.Normalized();
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
            Settings = _settings,
            PeerConnected = _peerConnected
        };

        RoomUpdated?.Invoke(_roomInfo);
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

        _roomInfo = _session.BuildInfo(string.Empty, _yourSlot);
        Publish();

        SendPeer(new PeerMessage
        {
            Type = "room",
            Room = _session.BuildInfo(string.Empty, 1 - _yourSlot)
        });
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
            _ = _js.InvokeVoidAsync("myakWebrtc.send", json).AsTask();
        }
        catch (JSException)
        {
            // Канал мог закрыться — следующий снимок покажет актуальное состояние.
        }
    }

    private void ResetPeer()
    {
        _peerStarted = false;
        _peerConnected = false;
        _session = null;
        _latestSnapshot = string.Empty;
        _lastTick = 0d;
        _lastSnapshotSent = 0d;
        _lastInputSent = 0d;
        _rttMilliseconds = -1;

        try
        {
            _ = _js.InvokeVoidAsync("myakWebrtc.close").AsTask();
        }
        catch (JSException)
        {
            // Страница уже выгружается.
        }
    }

    private RoomInfo BuildInfo() => new()
    {
        Code = string.Empty,
        Mode = "online",
        Status = _status,
        YourSlot = _yourSlot,
        IsHost = _isHost,
        HostKind = KindName(_hostKind),
        GuestKind = _isHost ? null : KindName(_guestKind),
        Settings = _settings,
        OpponentConnected = _peerConnected,
        PeerConnected = _peerConnected
    };

    private static string Encode(InviteCode invite) =>
        CodePrefix + Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(invite, JsonOptions)));

    private InviteCode Decode(string code)
    {
        var trimmed = (code ?? string.Empty).Trim();

        if (!trimmed.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Это не код MyaK Biplane. Скопируйте код целиком от соперника.");
        }

        InviteCode? invite;

        try
        {
            var json = Encoding.UTF8.GetString(
                Convert.FromBase64String(trimmed[CodePrefix.Length..]));
            invite = JsonSerializer.Deserialize<InviteCode>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new InvalidOperationException("Код повреждён или обрезан. Попросите соперника отправить его заново.");
        }

        if (invite is null || string.IsNullOrWhiteSpace(invite.Rtc))
        {
            throw new InvalidOperationException("В коде нет данных соединения.");
        }

        if (!string.Equals(invite.Version, _version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Версии не совпадают: у вас {_version}, у соперника {invite.Version}. " +
                "Обновите игру и попробуйте снова.");
        }

        return invite;
    }

    private static bool SameSettings(RoomSettings left, RoomSettings right) =>
        left.ScoreToWin == right.ScoreToWin
        && left.AllowSameKind == right.AllowSameKind
        && left.AllowEjection == right.AllowEjection;

    private static double NowMilliseconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static PlaneKind ParseKind(string kindName) =>
        Enum.TryParse<PlaneKind>(kindName, ignoreCase: true, out var kind)
            ? kind
            : throw new InvalidOperationException("Неизвестный класс летательного аппарата.");

    private static string KindName(PlaneKind kind) =>
        kind == PlaneKind.Biplane ? "biplane" : "pterodactyl";

    /// <summary>Сжатый пакет, которым игроки обмениваются вручную.</summary>
    private sealed class InviteCode
    {
        [JsonPropertyName("v")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("h")]
        public string HostKind { get; set; } = "biplane";

        [JsonPropertyName("g")]
        public string? GuestKind { get; set; }

        [JsonPropertyName("s")]
        public RoomSettings? Settings { get; set; }

        [JsonPropertyName("rtc")]
        public string Rtc { get; set; } = string.Empty;
    }
}
