using System.Text.Json;
using System.Text.Json.Serialization;
using MyaKBiplane.Core.Math;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Game;

/// <summary>
/// Локальная сессия матча с ботом: фиксированный шаг симуляции и снимки состояния.
/// </summary>
/// <remarks>
/// Используется тестами и может пригодиться для полностью оффлайн-режима.
/// В сетевой игре шаг выполняет сервер.
/// </remarks>
public sealed class GameSession
{
    private const float FixedStep = 1f / 60f;
    private const int MaxPendingEvents = 256;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GameWorld _world;
    private readonly BotController _bot;
    private readonly List<GameEvent> _pendingEvents = new(64);
    private float _accumulator;

    /// <summary>
    /// Создаёт сессию и запускает новый матч.
    /// </summary>
    /// <param name="playerKind">Класс аппарата игрока.</param>
    /// <param name="seed">Зерно случайных чисел.</param>
    public GameSession(PlaneKind playerKind, int seed = 0)
    {
        PlayerKind = playerKind;
        _world = new GameWorld(seed: seed);
        _bot = new BotController(seed + 1, _world.Balance);
        Reset(playerKind);
    }

    /// <summary>Класс аппарата игрока.</summary>
    public PlaneKind PlayerKind { get; private set; }

    /// <summary>Текущее состояние мира.</summary>
    public GameWorld World => _world;

    /// <summary>Завершён ли матч.</summary>
    public bool IsFinished => _world.Status == MatchStatus.Finished;

    /// <summary>
    /// Начинает новый матч.
    /// </summary>
    /// <param name="playerKind">Класс аппарата игрока. Если не задан, используется текущий.</param>
    public void Reset(PlaneKind? playerKind = null)
    {
        PlayerKind = playerKind ?? PlayerKind;
        _world.Reset(PlayerKind, GameWorld.Opposite(PlayerKind), secondIsBot: true);
        _bot.Reset();
        _pendingEvents.Clear();
        _accumulator = 0f;
    }

    /// <summary>
    /// Обновляет сессию на основе ввода игрока и прошедшего времени.
    /// </summary>
    /// <param name="input">Ввод игрока.</param>
    /// <param name="deltaSeconds">Время с предыдущего кадра в секундах.</param>
    public void Update(PlayerInput input, float deltaSeconds)
    {
        _accumulator += GameMath.Clamp(deltaSeconds, 0f, 0.25f);
        var steps = 0;

        while (_accumulator >= FixedStep && steps < 5 && !IsFinished)
        {
            var botInput = _bot.Update(_world, FixedStep);
            _world.Step(input, botInput, FixedStep);

            foreach (var gameEvent in _world.Events)
            {
                _pendingEvents.Add(gameEvent);
            }

            if (_pendingEvents.Count > MaxPendingEvents)
            {
                _pendingEvents.RemoveRange(0, _pendingEvents.Count - MaxPendingEvents);
            }

            _accumulator -= FixedStep;
            steps++;
        }

        if (steps == 5)
        {
            _accumulator = 0f;
        }
    }

    /// <summary>
    /// Создаёт снимок состояния для рендерера.
    /// </summary>
    /// <returns>Снимок текущего кадра.</returns>
    public GameSnapshot CreateSnapshot()
    {
        var status = IsFinished ? "finished" : "playing";
        var snapshot = SnapshotFactory.Create(_world, yourSlot: 0, status, _pendingEvents);
        _pendingEvents.Clear();
        return snapshot;
    }

    /// <summary>
    /// Создаёт JSON-снимок состояния для JavaScript-рендерера.
    /// </summary>
    /// <returns>JSON-строка снимка.</returns>
    public string CreateSnapshotJson() => JsonSerializer.Serialize(CreateSnapshot(), SerializerOptions);
}
