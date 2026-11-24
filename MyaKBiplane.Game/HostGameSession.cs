using System.Text.Json;
using System.Text.Json.Serialization;
using MyaKBiplane.Core.Math;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Game;

/// <summary>
/// Авторитетная симуляция P2P-матча: работает в браузере хозяина комнаты.
/// </summary>
/// <remarks>
/// Сервер больше не считает матч — он только выдаёт код и ретранслирует SDP/ICE.
/// Хост получает ввод обоих игроков, шагает миром с фиксированным шагом 60 Гц
/// и рассылает снимки напрямую сопернику через DataChannel.
/// </remarks>
public sealed class HostGameSession
{
    private const float FixedStep = 1f / 60f;
    private const float MaxDeltaSeconds = 0.25f;
    private const int MaxStepsPerFrame = 5;
    private const int MaxPendingEvents = 512;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GameWorld _world;
    private readonly PlaneKind _hostKind;
    private readonly PlaneKind _guestKind;
    private readonly InputState _hostInput = new();
    private readonly InputState _guestInput = new();
    private readonly List<GameEvent> _events = new(128);
    private float _accumulator;

    /// <summary>
    /// Создаёт сессию хоста с классами аппаратов обоих игроков.
    /// </summary>
    /// <param name="hostKind">Класс аппарата хозяина.</param>
    /// <param name="guestKind">Класс аппарата гостя.</param>
    /// <param name="seed">Зерно случайных чисел.</param>
    public HostGameSession(PlaneKind hostKind, PlaneKind guestKind, int seed = 0)
    {
        _hostKind = hostKind;
        _guestKind = guestKind;
        _world = new GameWorld(seed: seed);
    }

    /// <summary>Статус матча: <c>waiting</c>, <c>playing</c>, <c>paused</c> или <c>finished</c>.</summary>
    public string Status { get; private set; } = "waiting";

    /// <summary>Кто поставил игру на паузу, или -1.</summary>
    public int PausedBySlot { get; private set; } = -1;

    /// <summary>Слот, предложивший реванш.</summary>
    public int? RematchOfferSlot { get; private set; }

    /// <summary>Уведомление для обоих игроков.</summary>
    public string? Notice { get; private set; }

    /// <summary>Слот, которому адресовано уведомление.</summary>
    public int? NoticeSlot { get; private set; }

    /// <summary>Завершён ли матч.</summary>
    public bool IsFinished => _world.Status == MatchStatus.Finished;

    /// <summary>Класс аппарата хозяина.</summary>
    public PlaneKind HostKind => _hostKind;

    /// <summary>Класс аппарата гостя.</summary>
    public PlaneKind GuestKind => _guestKind;

    /// <summary>Начинает матч с реальными классами обоих игроков.</summary>
    public void Start()
    {
        _world.Reset(_hostKind, _guestKind, secondIsBot: false);
        ClearInput(_hostInput);
        ClearInput(_guestInput);
        _events.Clear();
        _accumulator = 0f;
        Status = "playing";
        PausedBySlot = -1;
        RematchOfferSlot = null;
        Notice = null;
        NoticeSlot = null;
    }

    /// <summary>Сохраняет ввод указанного слота.</summary>
    /// <param name="slot">Слот игрока: 0 — хост, 1 — гость.</param>
    /// <param name="input">Ввод.</param>
    public void SetInput(int slot, InputState input)
    {
        var target = slot == 0 ? _hostInput : _guestInput;

        target.Thrust = input.Thrust;
        target.Turn = input.Turn;
        target.MoveX = input.MoveX;
        target.MoveY = input.MoveY;
        target.AimX = input.AimX;
        target.AimY = input.AimY;
        target.Fire = input.Fire;
        target.Ability = input.Ability;

        // Разовые команды накапливаются до ближайшего шага симуляции,
        // чтобы нажатие между кадрами не потерялось.
        target.Reload |= input.Reload;
        target.Eject |= input.Eject;
        target.Parachute |= input.Parachute;
    }

    /// <summary>Продвигает симуляцию на прошедшее время.</summary>
    /// <param name="deltaSeconds">Время с предыдущего кадра в секундах.</param>
    public void Update(float deltaSeconds)
    {
        if (Status != "playing")
        {
            return;
        }

        _accumulator += GameMath.Clamp(deltaSeconds, 0f, MaxDeltaSeconds);
        var steps = 0;

        while (_accumulator >= FixedStep && steps < MaxStepsPerFrame && !IsFinished)
        {
            _world.Step(ToPlayerInput(_hostInput), ToPlayerInput(_guestInput), FixedStep);

            // Команды действуют ровно один шаг: иначе зажатая R сразу же
            // начинала бы следующую перезарядку.
            ClearOneShots(_hostInput);
            ClearOneShots(_guestInput);

            foreach (var gameEvent in _world.Events)
            {
                _events.Add(gameEvent);
            }

            if (_events.Count > MaxPendingEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxPendingEvents);
            }

            _accumulator -= FixedStep;
            steps++;
        }

        if (steps == MaxStepsPerFrame)
        {
            _accumulator = 0f;
        }

        if (IsFinished)
        {
            Status = "finished";
            PausedBySlot = -1;
        }
    }

    /// <summary>Создаёт JSON-снимок для указанного игрока.</summary>
    /// <param name="slot">Слот получателя.</param>
    /// <returns>Снимок для рендерера.</returns>
    public string CreateSnapshotJson(int slot)
    {
        var status = IsFinished ? "finished" : Status;
        var snapshot = SnapshotFactory.Create(_world, slot, status, _events);
        _events.Clear();
        return JsonSerializer.Serialize(snapshot, SerializerOptions);
    }

    /// <summary>
    /// Создаёт пару JSON-снимков для хоста и гостя из одного набора событий.
    /// </summary>
    /// <returns>Снимок для слота 0 и снимок для слота 1.</returns>
    public (string Host, string Guest) CreateSnapshotPairJson()
    {
        var status = IsFinished ? "finished" : Status;
        var host = SnapshotFactory.Create(_world, 0, status, _events);
        var guest = SnapshotFactory.Create(_world, 1, status, _events);
        _events.Clear();

        return (
            JsonSerializer.Serialize(host, SerializerOptions),
            JsonSerializer.Serialize(guest, SerializerOptions));
    }

    /// <summary>Ставит игру на паузу или снимает её.</summary>
    /// <param name="slot">Слот инициатора.</param>
    /// <returns>Изменился ли статус.</returns>
    public bool TogglePause(int slot)
    {
        if (Status == "playing")
        {
            Status = "paused";
            PausedBySlot = slot;
            return true;
        }

        if (Status == "paused")
        {
            Status = "playing";
            PausedBySlot = -1;
            return true;
        }

        return false;
    }

    /// <summary>Предлагает реванш.</summary>
    /// <param name="slot">Слот инициатора.</param>
    /// <returns>Изменилось ли состояние.</returns>
    public bool OfferRematch(int slot)
    {
        if (Status == "waiting")
        {
            return false;
        }

        // Оба игрока предложили — реванш начинается сразу.
        if (RematchOfferSlot is not null && RematchOfferSlot != slot)
        {
            Start();
            return true;
        }

        RematchOfferSlot = slot;
        Notice = null;
        NoticeSlot = null;
        return true;
    }

    /// <summary>Отвечает на предложение реванша.</summary>
    /// <param name="slot">Слот отвечающего.</param>
    /// <param name="accept">Принять ли реванш.</param>
    /// <returns>Изменилось ли состояние.</returns>
    public bool RespondRematch(int slot, bool accept)
    {
        if (RematchOfferSlot is null || RematchOfferSlot == slot)
        {
            return false;
        }

        if (accept)
        {
            Start();
        }
        else
        {
            Notice = "Соперник отклонил реванш";
            NoticeSlot = RematchOfferSlot;
            RematchOfferSlot = null;
        }

        return true;
    }

    /// <summary>Формирует состояние матча для указанного игрока.</summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="slot">Слот получателя.</param>
    /// <returns>Информация для клиента.</returns>
    public RoomInfo BuildInfo(string code, int slot) => new()
    {
        Code = code,
        Mode = "online",
        Status = Status,
        YourSlot = slot,
        IsHost = slot == 0,
        HostKind = SnapshotFactory.ToKindName(_hostKind),
        GuestKind = SnapshotFactory.ToKindName(_guestKind),
        OpponentConnected = true,
        PeerConnected = true,
        PausedBySlot = PausedBySlot,
        RematchOfferedByYou = RematchOfferSlot == slot,
        RematchOfferedByOpponent = RematchOfferSlot is not null && RematchOfferSlot != slot,
        Notice = NoticeSlot is null || NoticeSlot == slot ? Notice : null
    };

    private static void ClearInput(InputState input)
    {
        input.Thrust = 0;
        input.Turn = 0;
        input.MoveX = 0;
        input.MoveY = 0;
        input.AimX = 0;
        input.AimY = 0;
        input.Fire = false;
        input.Ability = false;
        ClearOneShots(input);
    }

    private static void ClearOneShots(InputState input)
    {
        input.Reload = false;
        input.Eject = false;
        input.Parachute = false;
    }

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
