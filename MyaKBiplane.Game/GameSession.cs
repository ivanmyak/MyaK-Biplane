using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Math;
using MyaKBiplane.Core.Simulation;

namespace MyaKBiplane.Game;

/// <summary>
/// Клиентская сессия матча: фиксированный шаг симуляции, бот и визуальные эффекты.
/// </summary>
/// <remarks>
/// Сессия не зависит от Blazor и конкретного рендерера. Это позволяет
/// переиспользовать её в веб-клиенте, MAUI-приложении и тестах.
/// </remarks>
public sealed class GameSession
{
    private const float FixedStep = 1f / 60f;
    private const int MaxParticles = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GameWorld _world;
    private readonly BotController _bot;
    private readonly Random _random;
    private readonly List<Particle> _particles = new(128);

    private float _accumulator;
    private bool _restartHeld;

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
        _random = new Random(seed + 2);
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
        _world.Reset(PlayerKind);
        _bot.Reset();
        _particles.Clear();
        _accumulator = 0f;
        _restartHeld = false;
    }

    /// <summary>
    /// Обновляет сессию на основе ввода игрока и прошедшего времени.
    /// </summary>
    /// <param name="input">Ввод игрока.</param>
    /// <param name="deltaSeconds">Время с предыдущего кадра в секундах.</param>
    public void Update(PlayerInput input, float deltaSeconds)
    {
        var restartPressed = input.Restart && !_restartHeld;
        _restartHeld = input.Restart;

        if (restartPressed && IsFinished)
        {
            Reset();
        }

        _accumulator += GameMath.Clamp(deltaSeconds, 0f, 0.25f);
        var steps = 0;

        while (_accumulator >= FixedStep && steps < 5 && !IsFinished)
        {
            var botInput = _bot.Update(_world, FixedStep);
            _world.Step(input, botInput, FixedStep);
            ProcessEvents();
            _accumulator -= FixedStep;
            steps++;
        }

        if (steps == 5)
        {
            _accumulator = 0f;
        }

        UpdateParticles(GameMath.Clamp(deltaSeconds, 0f, 0.1f));
    }

    /// <summary>
    /// Создаёт снимок состояния для рендерера.
    /// </summary>
    /// <returns>Снимок текущего кадра.</returns>
    public GameSnapshot CreateSnapshot()
    {
        var planes = new List<SnapshotPlane>(_world.Planes.Count);
        foreach (var plane in _world.Planes)
        {
            planes.Add(new SnapshotPlane
            {
                Id = plane.Id,
                X = plane.Position.X,
                Y = plane.Position.Y,
                Angle = plane.Rotation,
                Health = plane.Health,
                MaxHealth = GameBalance.MaxHealth,
                Kind = ToKindName(plane.Kind),
                IsPlayer = plane.IsPlayer,
                Alive = plane.IsAlive,
                Score = plane.Score,
                Boost = plane.BoostTimeRemaining,
                BoostCooldown = plane.BoostCooldownRemaining,
                DashCooldown = plane.DashCooldownRemaining,
                Charge = plane.Charge,
                Reload = plane.ReloadRemaining,
                HitFlash = plane.HitFlash
            });
        }

        var projectiles = new List<SnapshotProjectile>(_world.Projectiles.Count);
        foreach (var projectile in _world.Projectiles)
        {
            projectiles.Add(new SnapshotProjectile
            {
                X = projectile.Position.X,
                Y = projectile.Position.Y,
                Angle = projectile.Rotation,
                Kind = projectile.Kind == ProjectileKind.Spear ? "spear" : "bullet",
                IsPlayer = projectile.OwnerIsPlayer,
                Length = projectile.Kind == ProjectileKind.Spear ? 40f : 16f
            });
        }

        var particles = new List<SnapshotParticle>(_particles.Count);
        foreach (var particle in _particles)
        {
            particles.Add(new SnapshotParticle
            {
                X = particle.Position.X,
                Y = particle.Position.Y,
                Size = particle.Size,
                Color = particle.Color,
                Alpha = particle.MaxLife <= 0f ? 0f : particle.Life / particle.MaxLife
            });
        }

        return new GameSnapshot
        {
            Status = _world.Status == MatchStatus.Finished ? "finished" : "playing",
            Winner = ToWinnerName(_world.Winner),
            PlayerScore = _world.Player.Score,
            BotScore = _world.Bot.Score,
            Elapsed = _world.Elapsed,
            WorldWidth = GameBalance.WorldWidth,
            WorldHeight = GameBalance.WorldHeight,
            PlayerKind = ToKindName(_world.Player.Kind),
            BotKind = ToKindName(_world.Bot.Kind),
            Planes = planes,
            Projectiles = projectiles,
            Particles = particles
        };
    }

    /// <summary>
    /// Создаёт JSON-снимок состояния для JavaScript-рендерера.
    /// </summary>
    /// <returns>JSON-строка снимка.</returns>
    public string CreateSnapshotJson() => JsonSerializer.Serialize(CreateSnapshot(), SerializerOptions);

    private void ProcessEvents()
    {
        foreach (var gameEvent in _world.Events)
        {
            switch (gameEvent.Type)
            {
                case GameEventType.Shot:
                    SpawnBurst(gameEvent.Position, 3, gameEvent.IsPlayer ? "#67e8f9" : "#fca5a5", 90f, 0.18f, 2.5f);
                    break;
                case GameEventType.Hit:
                    SpawnBurst(gameEvent.Position, 12, "#fbbf24", 170f, 0.35f, 3f);
                    break;
                case GameEventType.Explosion:
                    SpawnBurst(gameEvent.Position, 42, "#fb923c", 300f, 0.8f, 4.5f);
                    break;
                case GameEventType.Boost:
                    SpawnBurst(gameEvent.Position, 14, "#22d3ee", 180f, 0.45f, 3f);
                    break;
                case GameEventType.Dash:
                    SpawnBurst(gameEvent.Position, 12, "#a7f3d0", 220f, 0.35f, 3f);
                    break;
                case GameEventType.Respawn:
                    SpawnBurst(gameEvent.Position, 20, "#a7f3d0", 140f, 0.6f, 3.5f);
                    break;
            }
        }
    }

    private void SpawnBurst(Vector2 position, int count, string color, float speed, float life, float size)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(_random.NextDouble() * GameMath.TwoPi);
            var velocity = GameMath.Direction(angle) * ((float)_random.NextDouble() * speed);
            var particleLife = life * (0.6f + ((float)_random.NextDouble() * 0.4f));

            _particles.Add(new Particle
            {
                Position = position,
                Velocity = velocity,
                Life = particleLife,
                MaxLife = particleLife,
                Size = size * (0.6f + ((float)_random.NextDouble() * 0.4f)),
                Color = color
            });
        }

        while (_particles.Count > MaxParticles)
        {
            _particles.RemoveAt(0);
        }
    }

    private void UpdateParticles(float deltaSeconds)
    {
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.Life -= deltaSeconds;

            if (particle.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }

            particle.Position += particle.Velocity * deltaSeconds;
            particle.Velocity *= MathF.Max(0f, 1f - (3f * deltaSeconds));
        }
    }

    private static string ToKindName(PlaneKind kind) =>
        kind == PlaneKind.Biplane ? "biplane" : "pterodactyl";

    private static string ToWinnerName(MatchWinner winner) => winner switch
    {
        MatchWinner.Player => "player",
        MatchWinner.Bot => "bot",
        MatchWinner.Draw => "draw",
        _ => "none"
    };

    private sealed class Particle
    {
        public Vector2 Position { get; set; }

        public Vector2 Velocity { get; set; }

        public float Life { get; set; }

        public float MaxLife { get; set; }

        public float Size { get; set; }

        public string Color { get; set; } = "#ffffff";
    }
}
