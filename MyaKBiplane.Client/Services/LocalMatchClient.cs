using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Client.Services;

/// <summary>
/// Матч с ботом, который целиком считается в браузере: сервер и сеть не нужны.
/// </summary>
/// <remarks>
/// Симуляция продвигается прямо из кадра рендерера, поэтому матч работает офлайн,
/// в самолёте и на мобильной сети, где игровой сервер недоступен.
/// </remarks>
public sealed class LocalMatchClient : IMatchClient
{
    private const double SnapshotIntervalMs = 33d;

    private readonly PlaneKind _playerKind;
    private readonly RoomSettings _settings;
    private GameSession? _session;
    private RoomInfo _room = new();
    private string _snapshotJson = string.Empty;
    private double _lastTimestamp = double.NaN;
    private double _lastSnapshotAt = double.NegativeInfinity;

    /// <summary>
    /// Создаёт локальный матч.
    /// </summary>
    /// <param name="playerKind">Класс аппарата игрока.</param>
    /// <param name="settings">Правила комнаты. Если не заданы, используются стандартные.</param>
    public LocalMatchClient(PlaneKind playerKind, RoomSettings? settings = null)
    {
        _playerKind = playerKind;
        _settings = (settings ?? RoomSettings.Default).Normalized();
    }

    /// <inheritdoc />
    public event Action<RoomInfo>? RoomUpdated;

    /// <inheritdoc />
    public event Action<string>? SnapshotReceived;

    /// <inheritdoc />
#pragma warning disable CS0067 // локальный матч не закрывается и не теряет соединение
    public event Action<string>? RoomClosed;

    /// <inheritdoc />
    public event Action<string>? ConnectionClosed;
#pragma warning restore CS0067

    /// <inheritdoc />
    public Uri? HubUri => null;

    /// <inheritdoc />
    public Task ConnectAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task<RoomInfo> CreateBotRoomAsync(string kind)
    {
        _session = new GameSession(ParseKind(kind), seed: Environment.TickCount, _settings);
        _snapshotJson = _session.CreateSnapshotJson();
        _room = BuildRoom("playing");
        _lastTimestamp = double.NaN;
        _lastSnapshotAt = double.NegativeInfinity;

        RoomUpdated?.Invoke(_room);
        return Task.FromResult(_room);
    }

    /// <inheritdoc />
    public Task<RoomInfo> CreateRoomAsync(string kind, RoomSettings settings) =>
        throw new NotSupportedException("Локальный матч не создаёт сетевых комнат.");

    /// <inheritdoc />
    public Task<RoomInfo> JoinRoomAsync(string code, string kind) =>
        throw new NotSupportedException("Локальный матч не подключается к комнатам.");

    /// <inheritdoc />
    public string Tick(InputState input, double timestamp)
    {
        if (_session is null || _room.Status != "playing")
        {
            return _snapshotJson;
        }

        // Первый кадр не двигает мир: иначе dt был бы огромным после загрузки.
        var delta = double.IsNaN(_lastTimestamp)
            ? 0d
            : Math.Min(0.1d, (timestamp - _lastTimestamp) / 1000d);
        _lastTimestamp = timestamp;

        _session.Update(ToPlayerInput(input), (float)delta);

        if (_session.IsFinished)
        {
            _room = BuildRoom("finished");
            _snapshotJson = _session.CreateSnapshotJson();
            RoomUpdated?.Invoke(_room);
            SnapshotReceived?.Invoke(_snapshotJson);
            return _snapshotJson;
        }

        if (timestamp - _lastSnapshotAt >= SnapshotIntervalMs)
        {
            _lastSnapshotAt = timestamp;
            _snapshotJson = _session.CreateSnapshotJson();
            SnapshotReceived?.Invoke(_snapshotJson);
        }

        return _snapshotJson;
    }

    /// <inheritdoc />
    public Task TogglePauseAsync()
    {
        var status = _room.Status switch
        {
            "playing" => "paused",
            "paused" => "playing",
            _ => _room.Status
        };

        if (status == _room.Status)
        {
            return Task.CompletedTask;
        }

        _room = BuildRoom(status);

        // После паузы отсчёт кадров начинается заново, иначе мир прыгнет вперёд.
        _lastTimestamp = double.NaN;
        RoomUpdated?.Invoke(_room);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OfferRematchAsync()
    {
        if (_session is null)
        {
            return Task.CompletedTask;
        }

        _session.Reset(_playerKind);
        _snapshotJson = _session.CreateSnapshotJson();
        _room = BuildRoom("playing");
        _lastTimestamp = double.NaN;
        _lastSnapshotAt = double.NegativeInfinity;

        RoomUpdated?.Invoke(_room);
        SnapshotReceived?.Invoke(_snapshotJson);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RespondRematchAsync(bool accept) => Task.CompletedTask;

    /// <inheritdoc />
    public Task LeaveRoomAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task<double> MeasureRttAsync() => Task.FromResult(-1d);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private RoomInfo BuildRoom(string status)
    {
        var playerKind = _session?.PlayerKind ?? _playerKind;

        return new RoomInfo
        {
            Code = string.Empty,
            Mode = "bot",
            Status = status,
            YourSlot = 0,
            IsHost = true,
            HostKind = SnapshotFactory.ToKindName(playerKind),
            GuestKind = SnapshotFactory.ToKindName(GameWorld.Opposite(playerKind)),
            Settings = _settings,
            OpponentConnected = true,
            PausedBySlot = status == "paused" ? 0 : -1
        };
    }

    private static PlaneKind ParseKind(string kindName) =>
        Enum.TryParse<PlaneKind>(kindName, ignoreCase: true, out var kind)
            ? kind
            : PlaneKind.Biplane;

    private static PlayerInput ToPlayerInput(InputState input) => new()
    {
        Thrust = input.Thrust,
        Turn = input.Turn,
        MoveX = input.MoveX,
        MoveY = input.MoveY,
        AimX = input.AimX,
        AimY = input.AimY,
        Fire = input.Fire,
        Ability = input.Ability,
        Reload = input.Reload,
        Eject = input.Eject,
        Parachute = input.Parachute
    };
}
