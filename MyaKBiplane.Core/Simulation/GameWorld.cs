using System.Numerics;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Math;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Детерминированная симуляция одного матча 1v1.
/// </summary>
/// <remarks>
/// Мир не знает о рендеринге, сети и платформе. Сервер и клиенты могут
/// использовать один и тот же экземпляр для расчёта состояния.
/// </remarks>
public sealed class GameWorld
{
    private readonly List<Plane> _planes = new(2);
    private readonly List<Projectile> _projectiles = new(64);
    private readonly List<GameEvent> _events = new(32);
    private int _nextProjectileId = 1;

    /// <summary>
    /// Создаёт мир и сразу расставляет биплана игрока против птеродактиля бота.
    /// </summary>
    /// <param name="balance">Параметры баланса. Если не заданы, используется <see cref="GameBalance.Default"/>.</param>
    /// <param name="seed">Начальное зерно случайных чисел.</param>
    public GameWorld(GameBalance? balance = null, int seed = 0)
    {
        Balance = balance ?? GameBalance.Default;
        Seed = seed;
        Reset(PlaneKind.Biplane);
    }

    /// <summary>Параметры баланса матча.</summary>
    public GameBalance Balance { get; }

    /// <summary>Зерно случайных чисел матча.</summary>
    public int Seed { get; }

    /// <summary>Все аппараты матча.</summary>
    public IReadOnlyList<Plane> Planes => _planes;

    /// <summary>Все активные снаряды.</summary>
    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    /// <summary>События, произошедшие за последний шаг.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Аппарат игрока.</summary>
    public Plane Player => _planes[0];

    /// <summary>Аппарат бота.</summary>
    public Plane Bot => _planes[1];

    /// <summary>Текущий статус матча.</summary>
    public MatchStatus Status { get; private set; }

    /// <summary>Победитель матча.</summary>
    public MatchWinner Winner { get; private set; }

    /// <summary>Время, прошедшее с начала матча.</summary>
    public float Elapsed { get; private set; }

    /// <summary>
    /// Начинает новый матч.
    /// </summary>
    /// <param name="playerKind">Класс аппарата игрока. Бот получает противоположный класс.</param>
    public void Reset(PlaneKind playerKind)
    {
        _planes.Clear();
        _projectiles.Clear();
        _events.Clear();
        _nextProjectileId = 1;
        Elapsed = 0f;
        Status = MatchStatus.Playing;
        Winner = MatchWinner.None;

        var player = new Plane(1, playerKind, true);
        var bot = new Plane(2, Opposite(playerKind), false);
        _planes.Add(player);
        _planes.Add(bot);

        Respawn(player);
        Respawn(bot);
        _events.Clear();
    }

    /// <summary>
    /// Выполняет один шаг симуляции.
    /// </summary>
    /// <param name="playerInput">Ввод игрока.</param>
    /// <param name="botInput">Ввод бота.</param>
    /// <param name="deltaSeconds">Длительность шага в секундах.</param>
    public void Step(PlayerInput playerInput, PlayerInput botInput, float deltaSeconds)
    {
        if (Status == MatchStatus.Finished || deltaSeconds <= 0f)
        {
            return;
        }

        var dt = GameMath.Clamp(deltaSeconds, 0f, 0.1f);
        Elapsed += dt;
        _events.Clear();

        UpdatePlane(Player, playerInput, dt);
        UpdatePlane(Bot, botInput, dt);
        UpdateProjectiles(dt);
    }

    /// <summary>
    /// Применяет урон к аппарату. Используется снарядами и тестами.
    /// </summary>
    /// <param name="target">Цель.</param>
    /// <param name="attacker">Атакующий аппарат, если известен.</param>
    /// <param name="damage">Количество урона.</param>
    /// <param name="hitPosition">Позиция попадания.</param>
    internal void ApplyDamage(Plane target, Plane? attacker, float damage, Vector2 hitPosition)
    {
        if (!target.IsAlive || Status == MatchStatus.Finished)
        {
            return;
        }

        target.Health = MathF.Max(0f, target.Health - damage);
        target.HitFlash = 0.18f;
        _events.Add(new GameEvent(GameEventType.Hit, hitPosition, damage, attacker?.IsPlayer ?? false));

        if (target.Health > 0f)
        {
            return;
        }

        target.RespawnRemaining = GameBalance.RespawnDelay;
        if (attacker is not null)
        {
            attacker.Score++;
        }

        _events.Add(new GameEvent(GameEventType.Explosion, target.Position, 0f, attacker?.IsPlayer ?? false));

        if (attacker is not null && attacker.Score >= GameBalance.ScoreToWin)
        {
            Status = MatchStatus.Finished;
            Winner = attacker.IsPlayer ? MatchWinner.Player : MatchWinner.Bot;
            _events.Add(new GameEvent(GameEventType.MatchFinished, target.Position, 0f, attacker.IsPlayer));
        }
    }

    private void UpdatePlane(Plane plane, PlayerInput input, float dt)
    {
        if (!plane.IsAlive)
        {
            plane.RespawnRemaining -= dt;
            if (plane.RespawnRemaining <= 0f)
            {
                Respawn(plane);
            }

            return;
        }

        plane.FireCooldown = MathF.Max(0f, plane.FireCooldown - dt);
        plane.ReloadRemaining = MathF.Max(0f, plane.ReloadRemaining - dt);
        plane.BoostTimeRemaining = MathF.Max(0f, plane.BoostTimeRemaining - dt);
        plane.BoostCooldownRemaining = MathF.Max(0f, plane.BoostCooldownRemaining - dt);
        plane.DashCooldownRemaining = MathF.Max(0f, plane.DashCooldownRemaining - dt);
        plane.HitFlash = MathF.Max(0f, plane.HitFlash - dt);

        if (plane.Kind == PlaneKind.Biplane)
        {
            UpdateBiplane(plane, input, dt);
        }
        else
        {
            UpdatePterodactyl(plane, input, dt);
        }

        KeepInsideWorld(plane);
        plane.WasFirePressed = input.Fire;
        plane.WasAbilityPressed = input.Ability;
    }

    private void UpdateBiplane(Plane plane, PlayerInput input, float dt)
    {
        var balance = Balance.Biplane;
        var isBoosting = plane.IsBoosting;

        plane.Rotation = GameMath.NormalizeAngle(
            plane.Rotation + (GameMath.Clamp(input.Turn, -1f, 1f) * balance.TurnRate * dt));

        var thrust = GameMath.Clamp(input.Thrust, -1f, 1f);
        if (thrust > 0f)
        {
            var acceleration = balance.Thrust * (isBoosting ? balance.BoostMultiplier : 1f);
            plane.Velocity += GameMath.Direction(plane.Rotation) * (acceleration * thrust * dt);
        }
        else if (thrust < 0f)
        {
            plane.Velocity += GameMath.Direction(plane.Rotation) * (balance.ReverseThrust * thrust * dt);
        }

        plane.Velocity += new Vector2(0f, balance.Gravity) * dt;
        plane.Velocity *= MathF.Max(0f, 1f - (balance.Drag * dt));
        plane.Velocity = GameMath.ClampLength(
            plane.Velocity,
            balance.MaxSpeed * (isBoosting ? balance.BoostMultiplier : 1f));
        plane.Position += plane.Velocity * dt;

        if (input.Ability && !plane.WasAbilityPressed && plane.BoostCooldownRemaining <= 0f)
        {
            plane.BoostTimeRemaining = balance.BoostDuration;
            plane.BoostCooldownRemaining = balance.BoostDuration + balance.BoostCooldown;
            _events.Add(new GameEvent(GameEventType.Boost, plane.Position, 0f, plane.IsPlayer));
        }

        if (input.Fire && plane.FireCooldown <= 0f)
        {
            var direction = GameMath.Direction(plane.Rotation);
            SpawnProjectile(
                ProjectileKind.Bullet,
                plane,
                plane.Position + (direction * (plane.Radius + 8f)),
                direction * balance.BulletSpeed,
                balance.BulletDamage,
                balance.BulletRadius,
                balance.BulletLifetime);
            plane.FireCooldown = balance.FireInterval;
        }
    }

    private void UpdatePterodactyl(Plane plane, PlayerInput input, float dt)
    {
        var balance = Balance.Pterodactyl;
        var move = new Vector2(
            GameMath.Clamp(input.MoveX, -1f, 1f),
            GameMath.Clamp(input.MoveY, -1f, 1f));

        if (move.LengthSquared() > 1f)
        {
            move = GameMath.NormalizeOrZero(move);
        }

        plane.Velocity += move * balance.Acceleration * dt;
        plane.Velocity *= MathF.Max(0f, 1f - (balance.Drag * dt));
        plane.Velocity = GameMath.ClampLength(plane.Velocity, balance.MaxSpeed);
        plane.Position += plane.Velocity * dt;

        var aim = new Vector2(input.AimX, input.AimY);
        var aimDelta = aim - plane.Position;
        if (aimDelta.LengthSquared() > 4f)
        {
            plane.Rotation = MathF.Atan2(aimDelta.Y, aimDelta.X);
        }
        else if (plane.Velocity.LengthSquared() > 4f)
        {
            plane.Rotation = MathF.Atan2(plane.Velocity.Y, plane.Velocity.X);
        }

        if (input.Ability && !plane.WasAbilityPressed && plane.DashCooldownRemaining <= 0f)
        {
            var dashDirection = move.LengthSquared() > 0.01f
                ? GameMath.NormalizeOrZero(move)
                : GameMath.Direction(plane.Rotation);

            plane.Velocity += dashDirection * balance.DashSpeed;
            plane.DashCooldownRemaining = balance.DashCooldown;
            _events.Add(new GameEvent(GameEventType.Dash, plane.Position, 0f, plane.IsPlayer));
        }

        if (plane.ReloadRemaining <= 0f)
        {
            if (input.Fire)
            {
                plane.Charge = MathF.Min(1f, plane.Charge + (dt / balance.ChargeTime));
            }
            else if (plane.WasFirePressed && plane.Charge > 0f)
            {
                var charge = plane.Charge;
                ThrowSpear(plane, charge);
                plane.Charge = 0f;
                plane.ReloadRemaining = GameMath.Lerp(balance.MinReload, balance.MaxReload, charge);
            }
        }
        else
        {
            plane.Charge = 0f;
        }
    }

    private void ThrowSpear(Plane plane, float charge)
    {
        var balance = Balance.Pterodactyl;
        var direction = GameMath.Direction(plane.Rotation);
        var speed = GameMath.Lerp(balance.MinSpearSpeed, balance.MaxSpearSpeed, charge);
        var damage = GameMath.Lerp(balance.MinDamage, balance.MaxDamage, charge);

        SpawnProjectile(
            ProjectileKind.Spear,
            plane,
            plane.Position + (direction * (plane.Radius + 10f)),
            direction * speed,
            damage,
            balance.SpearRadius,
            balance.SpearLifetime);
    }

    private void SpawnProjectile(
        ProjectileKind kind,
        Plane owner,
        Vector2 position,
        Vector2 velocity,
        float damage,
        float radius,
        float lifetime)
    {
        var projectile = new Projectile(
            _nextProjectileId++,
            kind,
            owner.Id,
            owner.IsPlayer,
            position,
            velocity,
            damage,
            radius,
            lifetime);

        _projectiles.Add(projectile);
        _events.Add(new GameEvent(GameEventType.Shot, position, 0f, owner.IsPlayer));
    }

    private void UpdateProjectiles(float dt)
    {
        for (var i = _projectiles.Count - 1; i >= 0; i--)
        {
            var projectile = _projectiles[i];
            projectile.Lifetime -= dt;

            if (projectile.Lifetime <= 0f)
            {
                _projectiles.RemoveAt(i);
                continue;
            }

            projectile.Position += projectile.Velocity * dt;

            if (IsOutsideWorld(projectile.Position, 80f))
            {
                _projectiles.RemoveAt(i);
                continue;
            }

            var target = projectile.OwnerIsPlayer ? Bot : Player;
            if (!target.IsAlive)
            {
                continue;
            }

            var hitRadius = projectile.Radius + target.Radius;
            if (Vector2.DistanceSquared(projectile.Position, target.Position) > hitRadius * hitRadius)
            {
                continue;
            }

            var attacker = projectile.OwnerIsPlayer ? Player : Bot;
            ApplyDamage(target, attacker, projectile.Damage, projectile.Position);
            _projectiles.RemoveAt(i);
        }
    }

    private void Respawn(Plane plane)
    {
        var x = plane.IsPlayer ? GameBalance.WorldWidth * 0.25f : GameBalance.WorldWidth * 0.75f;
        var rotation = plane.IsPlayer ? 0f : MathF.PI;
        plane.ResetForSpawn(new Vector2(x, GameBalance.WorldHeight * 0.5f), rotation);
        _events.Add(new GameEvent(GameEventType.Respawn, plane.Position, 0f, plane.IsPlayer));
    }

    private static void KeepInsideWorld(Plane plane)
    {
        var min = GameBalance.WorldPadding;
        var maxX = GameBalance.WorldWidth - min;
        var maxY = GameBalance.WorldHeight - min;

        if (plane.Position.X < min)
        {
            plane.Position = new Vector2(min, plane.Position.Y);
            plane.Velocity = new Vector2(MathF.Abs(plane.Velocity.X) * 0.4f, plane.Velocity.Y);
        }
        else if (plane.Position.X > maxX)
        {
            plane.Position = new Vector2(maxX, plane.Position.Y);
            plane.Velocity = new Vector2(-MathF.Abs(plane.Velocity.X) * 0.4f, plane.Velocity.Y);
        }

        if (plane.Position.Y < min)
        {
            plane.Position = new Vector2(plane.Position.X, min);
            plane.Velocity = new Vector2(plane.Velocity.X, MathF.Abs(plane.Velocity.Y) * 0.4f);
        }
        else if (plane.Position.Y > maxY)
        {
            plane.Position = new Vector2(plane.Position.X, maxY);
            plane.Velocity = new Vector2(plane.Velocity.X, -MathF.Abs(plane.Velocity.Y) * 0.4f);
        }
    }

    private static bool IsOutsideWorld(Vector2 position, float margin)
    {
        return position.X < -margin
            || position.Y < -margin
            || position.X > GameBalance.WorldWidth + margin
            || position.Y > GameBalance.WorldHeight + margin;
    }

    private static PlaneKind Opposite(PlaneKind kind) =>
        kind == PlaneKind.Biplane ? PlaneKind.Pterodactyl : PlaneKind.Biplane;
}
