using System.Numerics;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Math;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Детерминированная симуляция одного матча.
/// </summary>
/// <remarks>
/// Мир не знает о рендеринге, сети и платформе. Сервер и клиенты могут
/// использовать один и тот же экземпляр для расчёта состояния. В мире
/// одновременно живут лётчики двух слотов и все аппараты, включая бесхозные.
/// </remarks>
public sealed class GameWorld
{
    private readonly List<Plane> _planes = new(8);
    private readonly List<Projectile> _projectiles = new(64);
    private readonly List<GameEvent> _events = new(48);
    private readonly Pilot[] _pilots = new Pilot[2];
    private readonly int[] _scores = new int[2];
    private int _nextPlaneId = 1;
    private int _nextProjectileId = 1;

    /// <summary>
    /// Создаёт мир и сразу расставляет биплан игрока против птеродактиля бота.
    /// </summary>
    /// <param name="balance">Параметры баланса. Если не заданы, используется <see cref="GameBalance.Default"/>.</param>
    /// <param name="seed">Начальное зерно случайных чисел.</param>
    public GameWorld(GameBalance? balance = null, int seed = 0)
    {
        Balance = balance ?? GameBalance.Default;
        Seed = seed;
        Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: true);
    }

    /// <summary>Параметры баланса матча.</summary>
    public GameBalance Balance { get; }

    /// <summary>Зерно случайных чисел матча.</summary>
    public int Seed { get; }

    /// <summary>Все аппараты матча, включая бесхозные.</summary>
    public IReadOnlyList<Plane> Planes => _planes;

    /// <summary>Лётчики двух слотов.</summary>
    public IReadOnlyList<Pilot> Pilots => _pilots;

    /// <summary>Все активные снаряды.</summary>
    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    /// <summary>События, произошедшие за последний шаг.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Текущий статус матча.</summary>
    public MatchStatus Status { get; private set; }

    /// <summary>Слот победителя или -1, если победитель ещё не определён.</summary>
    public int WinnerSlot { get; private set; } = -1;

    /// <summary>Время, прошедшее с начала матча.</summary>
    public float Elapsed { get; private set; }

    /// <summary>Возвращает счёт указанного слота.</summary>
    /// <param name="slot">Слот 0 или 1.</param>
    /// <returns>Количество уничтоженных противников.</returns>
    public int Score(int slot) => _scores[slot];

    /// <summary>Возвращает лётчика указанного слота.</summary>
    /// <param name="slot">Слот 0 или 1.</param>
    /// <returns>Лётчик слота.</returns>
    public Pilot Pilot(int slot) => _pilots[slot];

    /// <summary>
    /// Возвращает активный аппарат слота, если он есть.
    /// </summary>
    /// <param name="slot">Слот 0 или 1.</param>
    /// <returns>Активный аппарат или <c>null</c>, если лётчик снаружи.</returns>
    public Plane? ActivePlane(int slot)
    {
        foreach (var plane in _planes)
        {
            if (plane.Slot == slot && !plane.IsAbandoned && plane.IsAlive)
            {
                return plane;
            }
        }

        return null;
    }

    /// <summary>Активный аппарат первого слота.</summary>
    public Plane? Player => ActivePlane(0);

    /// <summary>Активный аппарат второго слота.</summary>
    public Plane? Bot => ActivePlane(1);

    /// <summary>
    /// Начинает новый матч с двумя заданными классами аппаратов.
    /// </summary>
    /// <param name="firstKind">Класс аппарата первого слота.</param>
    /// <param name="secondKind">Класс аппарата второго слота.</param>
    /// <param name="secondIsBot">Управляется ли второй слот ботом.</param>
    public void Reset(PlaneKind firstKind, PlaneKind secondKind, bool secondIsBot = true)
    {
        _planes.Clear();
        _projectiles.Clear();
        _events.Clear();
        _nextPlaneId = 1;
        _nextProjectileId = 1;
        _scores[0] = 0;
        _scores[1] = 0;
        Elapsed = 0f;
        Status = MatchStatus.Playing;
        WinnerSlot = -1;

        _pilots[0] = new Pilot(0, false, firstKind);
        _pilots[1] = new Pilot(1, secondIsBot, secondKind);

        SpawnPlane(_pilots[0], announce: false);
        SpawnPlane(_pilots[1], announce: false);
        _events.Clear();
    }

    /// <summary>
    /// Начинает новый матч, где второй слот занимает бот противоположного класса.
    /// </summary>
    /// <param name="firstKind">Класс аппарата первого слота.</param>
    public void Reset(PlaneKind firstKind) => Reset(firstKind, Opposite(firstKind), secondIsBot: true);

    /// <summary>
    /// Выполняет один шаг симуляции.
    /// </summary>
    /// <param name="firstInput">Ввод первого слота.</param>
    /// <param name="secondInput">Ввод второго слота.</param>
    /// <param name="deltaSeconds">Длительность шага в секундах.</param>
    public void Step(PlayerInput firstInput, PlayerInput secondInput, float deltaSeconds)
    {
        if (Status == MatchStatus.Finished || deltaSeconds <= 0f)
        {
            return;
        }

        var dt = GameMath.Clamp(deltaSeconds, 0f, 0.1f);
        Elapsed += dt;
        _events.Clear();

        UpdatePilot(0, firstInput, dt);
        UpdatePilot(1, secondInput, dt);

        for (var i = _planes.Count - 1; i >= 0; i--)
        {
            var plane = _planes[i];

            if (plane.IsAbandoned && plane.IsAlive)
            {
                UpdateAbandonedPlane(plane, dt);
            }
        }

        UpdateProjectiles(dt);
    }

    /// <summary>
    /// Применяет урон к аппарату. Используется снарядами и тестами.
    /// </summary>
    /// <param name="target">Цель.</param>
    /// <param name="attackerSlot">Слот атакующего или -1.</param>
    /// <param name="damage">Количество урона.</param>
    /// <param name="hitPosition">Позиция попадания.</param>
    internal void ApplyDamage(Plane target, int attackerSlot, float damage, Vector2 hitPosition)
    {
        if (!target.IsAlive || target.IsProtected || Status == MatchStatus.Finished)
        {
            return;
        }

        target.Health = MathF.Max(0f, target.Health - damage);
        target.HitFlash = 0.18f;
        _events.Add(new GameEvent(GameEventType.Hit, hitPosition, damage, attackerSlot));

        if (target.Health <= 0f)
        {
            DestroyPlane(target, attackerSlot, crash: false);
        }
    }

    private void UpdatePilot(int slot, PlayerInput input, float dt)
    {
        var pilot = _pilots[slot];

        switch (pilot.State)
        {
            case PilotState.WaitingRespawn:
                pilot.RespawnRemaining -= dt;

                if (pilot.RespawnRemaining <= 0f)
                {
                    SpawnPlane(pilot);
                }

                pilot.WasEjectPressed = input.Eject;
                pilot.WasParachutePressed = false;
                return;

            case PilotState.InPlane:
            {
                var plane = ActivePlane(slot);

                if (plane is null)
                {
                    pilot.State = PilotState.WaitingRespawn;
                    pilot.RespawnRemaining = GameBalance.RespawnDelay;
                    pilot.WasEjectPressed = input.Eject;
                    pilot.WasParachutePressed = false;
                    return;
                }

                UpdatePlane(plane, input, dt);

                if (input.Eject
                    && !pilot.WasEjectPressed
                    && pilot.State == PilotState.InPlane
                    && plane.IsAlive
                    && !plane.IsAbandoned
                    && !plane.IsEntering)
                {
                    EjectPilot(pilot, plane);
                }

                pilot.WasEjectPressed = input.Eject;
                pilot.WasParachutePressed = false;
                return;
            }

            case PilotState.Falling:
            case PilotState.Parachuting:
            case PilotState.OnGround:
                UpdateFreePilot(pilot, input, dt);
                pilot.WasEjectPressed = input.Eject;
                pilot.WasParachutePressed = input.Parachute;
                return;
        }
    }

    private void UpdatePlane(Plane plane, PlayerInput input, float dt)
    {
        plane.FireCooldown = MathF.Max(0f, plane.FireCooldown - dt);
        plane.BoostTimeRemaining = MathF.Max(0f, plane.BoostTimeRemaining - dt);
        plane.BoostCooldownRemaining = MathF.Max(0f, plane.BoostCooldownRemaining - dt);
        plane.DashCooldownRemaining = MathF.Max(0f, plane.DashCooldownRemaining - dt);
        plane.HitFlash = MathF.Max(0f, plane.HitFlash - dt);
        plane.ProtectionRemaining = MathF.Max(0f, plane.ProtectionRemaining - dt);

        var wasReloading = plane.IsReloading;
        plane.ReloadRemaining = MathF.Max(0f, plane.ReloadRemaining - dt);

        if (wasReloading && !plane.IsReloading && plane.Kind == PlaneKind.Biplane)
        {
            plane.Ammo = plane.MagazineSize;
            _events.Add(new GameEvent(GameEventType.ReloadFinished, plane.Position, 0f, plane.Slot));
        }

        if (plane.IsEntering)
        {
            UpdateSpawnEntry(plane, dt);
            plane.WasFirePressed = input.Fire;
            plane.WasAbilityPressed = input.Ability;
            return;
        }

        if (plane.Kind == PlaneKind.Biplane)
        {
            UpdateBiplane(plane, input, dt);
        }
        else
        {
            UpdatePterodactyl(plane, input, dt);
        }

        KeepInsideWorld(plane);

        if (plane.Position.Y + plane.Radius >= GameBalance.GroundY)
        {
            if (plane.IsProtected)
            {
                plane.Position = new Vector2(plane.Position.X, GameBalance.GroundY - plane.Radius);
                plane.Velocity = new Vector2(plane.Velocity.X, MathF.Min(0f, plane.Velocity.Y));
            }
            else
            {
                // Падение на землю с лётчиком на борту засчитывается сопернику как уничтожение.
                DestroyPlane(plane, attackerSlot: OppositeSlot(plane.Slot), crash: true);
                return;
            }
        }

        plane.WasFirePressed = input.Fire;
        plane.WasAbilityPressed = input.Ability;
    }

    /// <summary>
    /// Проигрывает неуправляемый выезд аппарата на спавн.
    /// </summary>
    /// <param name="plane">Аппарат.</param>
    /// <param name="dt">Длительность шага.</param>
    private static void UpdateSpawnEntry(Plane plane, float dt)
    {
        plane.EntryRemaining = MathF.Max(0f, plane.EntryRemaining - dt);

        var duration = GameBalance.SpawnEntryDuration;
        var progress = duration <= 0f
            ? 1f
            : GameMath.Clamp(1f - (plane.EntryRemaining / duration), 0f, 1f);
        var end = SpawnPoint(plane.Slot);
        var finalRotation = SpawnApproachRotation(plane.Slot);

        if (plane.Kind == PlaneKind.Pterodactyl)
        {
            var eased = SmoothStep(progress);
            plane.Position = new Vector2(GameMath.Lerp(SpawnEntryX(plane.Slot), end.X, eased), end.Y);
            plane.Rotation = finalRotation;
        }
        else
        {
            // Выезд и взлёт описываются одной кривой Безье: аппарат плавно трогается
            // с места, отрывается от земли и подходит к точке спавна вдоль курса.
            // Разгон по земле и финальный подход задаются долями горизонтали,
            // поэтому подъём распределён по всему пути, а не собран в конце.
            var runRotation = SpawnRunRotation(plane.Slot);
            var start = new Vector2(SpawnEntryX(plane.Slot), GameBalance.GroundY - plane.Radius);
            var span = end.X - start.X;
            var rollPoint = new Vector2(
                start.X + (span * GameBalance.SpawnEntryRollShare),
                start.Y);
            var approachLength = MathF.Abs(span) * GameBalance.SpawnEntryApproachShare;
            var approachPoint = end - (GameMath.Direction(finalRotation) * approachLength);

            // Разметка времени подобрана так, чтобы к концу анимации скорость
            // совпадала с курсовой скоростью передачи управления.
            var endSpeedFactor = approachLength <= 0f
                ? 1f
                : GameBalance.SpawnEntrySpeed * duration / (3f * approachLength);

            var t = TakeoffProgress(progress, endSpeedFactor);
            plane.Position = CubicBezier(start, rollPoint, approachPoint, end, t);
            plane.Rotation = GameMath.Lerp(runRotation, finalRotation, SmoothStep(progress));
        }

        if (plane.EntryRemaining > 0f)
        {
            return;
        }

        plane.Position = end;
        plane.Rotation = finalRotation;
        plane.Velocity = GameMath.Direction(finalRotation) * GameBalance.SpawnEntrySpeed;
    }

    /// <summary>
    /// Переразметка времени взлёта: трогание с нуля и подход к концу с курсовой скоростью.
    /// </summary>
    /// <param name="progress">Линейный прогресс 0..1.</param>
    /// <param name="endSpeedFactor">Отношение конечной скорости кривой к средней.</param>
    /// <returns>Параметр кривой 0..1.</returns>
    private static float TakeoffProgress(float progress, float endSpeedFactor)
    {
        var t = GameMath.Clamp(progress, 0f, 1f);
        var cubic = endSpeedFactor - 2f;
        var quadratic = 3f - endSpeedFactor;
        return (cubic * t * t * t) + (quadratic * t * t);
    }

    /// <summary>
    /// Точка на кубической кривой Безье.
    /// </summary>
    /// <param name="p0">Начальная точка.</param>
    /// <param name="p1">Первая контрольная точка.</param>
    /// <param name="p2">Вторая контрольная точка.</param>
    /// <param name="p3">Конечная точка.</param>
    /// <param name="t">Параметр 0..1.</param>
    /// <returns>Точка кривой.</returns>
    private static Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var u = 1f - t;
        return (u * u * u * p0)
            + (3f * u * u * t * p1)
            + (3f * u * t * t * p2)
            + (t * t * t * p3);
    }

    /// <summary>
    /// Плавная интерполяция 0..1.
    /// </summary>
    /// <param name="value">Значение в диапазоне 0..1.</param>
    /// <returns>Сглаженное значение.</returns>
    private static float SmoothStep(float value)
    {
        var t = GameMath.Clamp(value, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>Точка, в которую аппарат прибывает после выезда.</summary>
    private static Vector2 SpawnPoint(int slot) => new(
        GameBalance.TowerX + (slot == 0 ? -GameBalance.SpawnOffsetX : GameBalance.SpawnOffsetX),
        GameBalance.SpawnY);

    /// <summary>Крайняя точка своей стороны, откуда начинается выезд.</summary>
    private static float SpawnEntryX(int slot) => slot == 0
        ? GameBalance.SpawnEntryInset
        : GameBalance.WorldWidth - GameBalance.SpawnEntryInset;

    /// <summary>Угол аппарата на земле до отрыва.</summary>
    private static float SpawnRunRotation(int slot) => slot == 0 ? 0f : MathF.PI;

    /// <summary>Угол набора высоты: нос смотрит на верхний угол соперника.</summary>
    private static float SpawnApproachRotation(int slot) => slot == 0
        ? -GameBalance.SpawnApproachAngle
        : MathF.PI + GameBalance.SpawnApproachAngle;

    /// <summary>Возвращает противоположный слот.</summary>
    private static int OppositeSlot(int slot) => slot == 0 ? 1 : 0;

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
            _events.Add(new GameEvent(GameEventType.Boost, plane.Position, 0f, plane.Slot));
        }

        if (input.Reload && !plane.IsReloading && plane.Ammo < plane.MagazineSize)
        {
            plane.ReloadRemaining = balance.ReloadDuration;
            _events.Add(new GameEvent(GameEventType.ReloadStarted, plane.Position, 0f, plane.Slot));
        }

        if (!plane.IsProtected
            && !plane.IsReloading
            && plane.Ammo > 0
            && input.Fire
            && plane.FireCooldown <= 0f)
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

            plane.Ammo--;
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
            _events.Add(new GameEvent(GameEventType.Dash, plane.Position, 0f, plane.Slot));
        }

        if (plane.IsProtected)
        {
            plane.Charge = 0f;
            return;
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

    private void UpdateAbandonedPlane(Plane plane, float dt)
    {
        plane.ProtectionRemaining = MathF.Max(0f, plane.ProtectionRemaining - dt);
        plane.Velocity += new Vector2(0f, GameBalance.AbandonedGravity) * dt;
        plane.Velocity *= MathF.Max(0f, 1f - (GameBalance.AbandonedDrag * dt));
        plane.Velocity = GameMath.ClampLength(plane.Velocity, GameBalance.AbandonedMaxSpeed);
        plane.Position += plane.Velocity * dt;

        if (plane.Velocity.LengthSquared() > 4f)
        {
            plane.Rotation = MathF.Atan2(plane.Velocity.Y, plane.Velocity.X);
        }

        KeepInsideWorld(plane);

        if (plane.Position.Y + plane.Radius < GameBalance.GroundY)
        {
            return;
        }

        if (plane.IsProtected)
        {
            plane.Position = new Vector2(plane.Position.X, GameBalance.GroundY - plane.Radius);
            plane.Velocity = new Vector2(plane.Velocity.X, MathF.Min(0f, plane.Velocity.Y));
            return;
        }

        DestroyPlane(plane, attackerSlot: -1, crash: true);
    }

    private void EjectPilot(Pilot pilot, Plane plane)
    {
        plane.IsAbandoned = true;
        pilot.State = PilotState.Falling;
        pilot.PlaneId = 0;
        pilot.Position = plane.Position + new Vector2(0f, -plane.Radius - GameBalance.PilotRadius);
        pilot.Velocity = (plane.Velocity * 0.2f) + new Vector2(0f, -GameBalance.PilotEjectImpulse);
        pilot.Facing = plane.Velocity.X >= 0f ? 1f : -1f;
        _events.Add(new GameEvent(GameEventType.Eject, pilot.Position, 0f, pilot.Slot));
    }

    private void UpdateFreePilot(Pilot pilot, PlayerInput input, float dt)
    {
        var moveX = GameMath.Clamp(
            MathF.Abs(input.MoveX) > 0.01f ? input.MoveX : input.Turn,
            -1f,
            1f);

        if (pilot.State is PilotState.Falling or PilotState.Parachuting)
        {
            if (input.Parachute && !pilot.WasParachutePressed)
            {
                pilot.State = pilot.State == PilotState.Parachuting
                    ? PilotState.Falling
                    : PilotState.Parachuting;
                _events.Add(new GameEvent(
                    pilot.State == PilotState.Parachuting
                        ? GameEventType.ParachuteOpened
                        : GameEventType.ParachuteClosed,
                    pilot.Position,
                    0f,
                    pilot.Slot));
            }

            UpdateAirbornePilot(pilot, moveX, dt);

            if (MathF.Abs(moveX) > 0.05f)
            {
                pilot.Facing = MathF.Sign(moveX);
            }

            if (pilot.Position.Y >= GameBalance.GroundY - GameBalance.PilotRadius)
            {
                pilot.Position = new Vector2(
                    pilot.Position.X,
                    GameBalance.GroundY - GameBalance.PilotRadius);
                var impactSpeed = pilot.Velocity.Y;
                pilot.Velocity = Vector2.Zero;

                if (impactSpeed > GameBalance.PilotSafeLandingSpeed)
                {
                    // Разбившийся лётчик приносит очко сопернику: у игрока всего две стороны,
                    // и смерть от собственной ошибки всё равно идёт в счёт противника.
                    KillPilot(pilot, attackerSlot: OppositeSlot(pilot.Slot), pilot.Position);
                }
                else
                {
                    pilot.State = PilotState.OnGround;
                    _events.Add(new GameEvent(GameEventType.Landed, pilot.Position, 0f, pilot.Slot));
                }
            }
        }
        else
        {
            pilot.Velocity = new Vector2(moveX * GameBalance.PilotGroundSpeed, 0f);
            pilot.Position += pilot.Velocity * dt;
            pilot.Position = new Vector2(
                GameMath.Clamp(
                    pilot.Position.X,
                    GameBalance.WorldPadding,
                    GameBalance.WorldWidth - GameBalance.WorldPadding),
                GameBalance.GroundY - GameBalance.PilotRadius);

            if (MathF.Abs(moveX) > 0.05f)
            {
                pilot.Facing = MathF.Sign(moveX);
            }
        }

        if (input.Eject
            && !pilot.WasEjectPressed
            && pilot.State is (PilotState.Falling or PilotState.Parachuting or PilotState.OnGround)
            && TryReenterPlane(pilot))
        {
            return;
        }

        if (pilot.State == PilotState.OnGround
            && MathF.Abs(pilot.Position.X - GameBalance.TowerX) <= GameBalance.PilotReachRadius
            && ActivePlane(pilot.Slot) is null)
        {
            _events.Add(new GameEvent(GameEventType.TowerReached, pilot.Position, 0f, pilot.Slot));
            SpawnPlane(pilot);
        }
    }

    /// <summary>
    /// Обновляет падение или планирование лётчика в воздухе.
    /// </summary>
    /// <param name="pilot">Лётчик.</param>
    /// <param name="moveX">Горизонтальный ввод -1..1.</param>
    /// <param name="dt">Длительность шага.</param>
    private static void UpdateAirbornePilot(Pilot pilot, float moveX, float dt)
    {
        if (pilot.State == PilotState.Parachuting)
        {
            var velocityX = pilot.Velocity.X
                + (moveX * GameBalance.PilotParachuteAcceleration * dt);
            velocityX *= MathF.Max(0f, 1f - (GameBalance.PilotFreefallDrag * 4f * dt));

            // Купол резко гасит падение до безопасной скорости снижения.
            var velocityY = MathF.Max(
                pilot.Velocity.Y - (GameBalance.PilotParachuteBrake * dt),
                GameBalance.PilotParachuteFallSpeed);

            pilot.Velocity = new Vector2(
                GameMath.Clamp(
                    velocityX,
                    -GameBalance.PilotParachuteMaxSpeed,
                    GameBalance.PilotParachuteMaxSpeed),
                velocityY);
        }
        else
        {
            var velocityX = pilot.Velocity.X
                + (moveX * GameBalance.PilotAirControlAcceleration * dt);
            velocityX *= MathF.Max(0f, 1f - (GameBalance.PilotFreefallDrag * dt));

            var velocityY = MathF.Min(
                pilot.Velocity.Y + (GameBalance.PilotGravity * dt),
                GameBalance.PilotTerminalSpeed);

            // Планирование: активное горизонтальное движение создаёт подъёмную силу
            // и гасит падение до безопасной скорости.
            if (MathF.Abs(moveX) > 0.3f)
            {
                velocityY = MathF.Min(velocityY, GameBalance.PilotGlideFallSpeed);
            }

            pilot.Velocity = new Vector2(
                GameMath.Clamp(velocityX, -GameBalance.PilotAirMaxSpeed, GameBalance.PilotAirMaxSpeed),
                velocityY);
        }

        pilot.Position += pilot.Velocity * dt;
        pilot.Position = new Vector2(
            GameMath.Clamp(
                pilot.Position.X,
                GameBalance.WorldPadding,
                GameBalance.WorldWidth - GameBalance.WorldPadding),
            pilot.Position.Y);
    }

    /// <summary>
    /// Убивает лётчика вне аппарата и запускает возрождение.
    /// </summary>
    /// <param name="pilot">Лётчик.</param>
    /// <param name="attackerSlot">Слот виновника или -1, если очко никому не начисляется.</param>
    /// <param name="position">Позиция гибели.</param>
    private void KillPilot(Pilot pilot, int attackerSlot, Vector2 position)
    {
        if (pilot.State is PilotState.InPlane or PilotState.WaitingRespawn)
        {
            return;
        }

        _events.Add(new GameEvent(GameEventType.PilotKilled, position, 0f, pilot.Slot));
        pilot.State = PilotState.WaitingRespawn;
        pilot.RespawnRemaining = GameBalance.RespawnDelay;
        pilot.Position = position;
        pilot.Velocity = Vector2.Zero;
        pilot.PlaneId = 0;

        if (attackerSlot >= 0 && attackerSlot != pilot.Slot)
        {
            AwardScore(attackerSlot, position);
        }
    }

    private bool TryReenterPlane(Pilot pilot)
    {
        if (pilot.State is not (PilotState.Falling or PilotState.Parachuting or PilotState.OnGround))
        {
            return false;
        }

        Plane? candidate = null;

        foreach (var plane in _planes)
        {
            if (plane.Slot == pilot.Slot
                && plane.IsAbandoned
                && plane.IsAlive
                && Vector2.DistanceSquared(plane.Position, pilot.Position)
                    <= GameBalance.PilotReenterRadius * GameBalance.PilotReenterRadius)
            {
                candidate = plane;
                break;
            }
        }

        if (candidate is null)
        {
            return false;
        }

        candidate.IsAbandoned = false;
        pilot.State = PilotState.InPlane;
        pilot.PlaneId = candidate.Id;
        pilot.Position = candidate.Position;
        pilot.Velocity = Vector2.Zero;
        _events.Add(new GameEvent(GameEventType.Reenter, candidate.Position, 0f, pilot.Slot));
        return true;
    }

    private Plane SpawnPlane(Pilot pilot, bool announce = true)
    {
        var plane = new Plane(_nextPlaneId++, pilot.Slot, pilot.Kind, pilot.IsBot);
        var startY = pilot.Kind == PlaneKind.Biplane
            ? GameBalance.GroundY - plane.Radius
            : GameBalance.SpawnY;

        plane.ResetForSpawn(
            new Vector2(SpawnEntryX(pilot.Slot), startY),
            SpawnRunRotation(pilot.Slot),
            Balance.Biplane.MagazineSize);

        _planes.Add(plane);
        pilot.State = PilotState.InPlane;
        pilot.PlaneId = plane.Id;
        pilot.Position = plane.Position;
        pilot.Velocity = Vector2.Zero;
        pilot.Facing = pilot.Slot == 0 ? 1f : -1f;
        pilot.RespawnRemaining = 0f;
        pilot.WasEjectPressed = false;
        pilot.WasParachutePressed = false;

        if (announce)
        {
            _events.Add(new GameEvent(GameEventType.Respawn, plane.Position, 0f, pilot.Slot));
        }

        return plane;
    }

    private void DestroyPlane(Plane plane, int attackerSlot, bool crash)
    {
        if (plane.IsDestroyed)
        {
            return;
        }

        plane.IsDestroyed = true;
        plane.Health = 0f;
        _planes.Remove(plane);

        _events.Add(new GameEvent(GameEventType.Explosion, plane.Position, 0f, plane.Slot));

        if (crash)
        {
            _events.Add(new GameEvent(GameEventType.Crash, plane.Position, 0f, plane.Slot));
        }

        var pilot = _pilots[plane.Slot];

        if (pilot.State == PilotState.InPlane && pilot.PlaneId == plane.Id)
        {
            pilot.State = PilotState.WaitingRespawn;
            pilot.RespawnRemaining = GameBalance.RespawnDelay;
            pilot.Position = plane.Position;
            pilot.Velocity = Vector2.Zero;
            pilot.PlaneId = 0;
        }

        if (attackerSlot < 0 || attackerSlot == plane.Slot)
        {
            return;
        }

        AwardScore(attackerSlot, plane.Position);
    }

    /// <summary>
    /// Начисляет очко слоту и завершает матч при достижении лимита.
    /// </summary>
    /// <param name="slot">Слот, получающий очко.</param>
    /// <param name="position">Позиция события для эффектов.</param>
    private void AwardScore(int slot, Vector2 position)
    {
        if (slot < 0 || slot >= _scores.Length || Status == MatchStatus.Finished)
        {
            return;
        }

        _scores[slot]++;

        if (_scores[slot] < GameBalance.ScoreToWin)
        {
            return;
        }

        Status = MatchStatus.Finished;
        WinnerSlot = slot;
        _events.Add(new GameEvent(GameEventType.MatchFinished, position, 0f, slot));
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
            owner.Slot,
            position,
            velocity,
            damage,
            radius,
            lifetime);

        _projectiles.Add(projectile);
        _events.Add(new GameEvent(GameEventType.Shot, position, 0f, owner.Slot));
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

            Plane? hit = null;

            foreach (var plane in _planes)
            {
                if (plane.Slot == projectile.OwnerSlot || !plane.IsAlive || plane.IsProtected)
                {
                    continue;
                }

                var hitRadius = projectile.Radius + plane.Radius;

                if (Vector2.DistanceSquared(projectile.Position, plane.Position) <= hitRadius * hitRadius)
                {
                    hit = plane;
                    break;
                }
            }

            if (hit is not null)
            {
                ApplyDamage(hit, projectile.OwnerSlot, projectile.Damage, projectile.Position);
                _projectiles.RemoveAt(i);
                continue;
            }

            Pilot? hitPilot = null;

            foreach (var pilot in _pilots)
            {
                if (pilot.Slot == projectile.OwnerSlot
                    || pilot.State is not (PilotState.Falling or PilotState.Parachuting or PilotState.OnGround))
                {
                    continue;
                }

                var pilotHitRadius = projectile.Radius + GameBalance.PilotRadius;

                if (Vector2.DistanceSquared(projectile.Position, pilot.Position)
                    <= pilotHitRadius * pilotHitRadius)
                {
                    hitPilot = pilot;
                    break;
                }
            }

            if (hitPilot is null)
            {
                continue;
            }

            KillPilot(hitPilot, projectile.OwnerSlot, projectile.Position);
            _projectiles.RemoveAt(i);
        }
    }

    private static void KeepInsideWorld(Plane plane)
    {
        var min = GameBalance.WorldPadding;
        var maxX = GameBalance.WorldWidth - min;

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
    }

    private static bool IsOutsideWorld(Vector2 position, float margin)
    {
        return position.X < -margin
            || position.Y < -margin
            || position.X > GameBalance.WorldWidth + margin
            || position.Y > GameBalance.WorldHeight + margin;
    }

    /// <summary>
    /// Возвращает противоположный класс аппарата.
    /// </summary>
    /// <param name="kind">Исходный класс.</param>
    /// <returns>Противоположный класс.</returns>
    public static PlaneKind Opposite(PlaneKind kind) =>
        kind == PlaneKind.Biplane ? PlaneKind.Pterodactyl : PlaneKind.Biplane;
}
