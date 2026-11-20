using System.Numerics;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Math;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Простой бот, управляющий аппаратом через тот же <see cref="PlayerInput"/>, что и игрок.
/// </summary>
/// <remarks>
/// Бот не читает скрытое состояние и не применяет урон напрямую. Это позволяет
/// использовать его в оффлайн-режиме и в headless-симуляции баланса.
/// </remarks>
public sealed class BotController
{
    private readonly GameBalance _balance;
    private readonly Random _random;
    private PlayerInput _cachedInput;
    private float _decisionTimer;
    private bool _chargeHeld;

    /// <summary>
    /// Создаёт бота.
    /// </summary>
    /// <param name="seed">Зерно случайных чисел для небольшого разброса прицела.</param>
    /// <param name="balance">Параметры баланса.</param>
    public BotController(int seed = 1, GameBalance? balance = null)
    {
        _random = new Random(seed);
        _balance = balance ?? GameBalance.Default;
    }

    /// <summary>
    /// Сбрасывает внутреннее состояние бота перед новым матчем.
    /// </summary>
    public void Reset()
    {
        _cachedInput = default;
        _decisionTimer = 0f;
        _chargeHeld = false;
    }

    /// <summary>
    /// Вычисляет ввод бота на текущем шаге.
    /// </summary>
    /// <param name="world">Текущее состояние мира.</param>
    /// <param name="deltaSeconds">Длительность шага.</param>
    /// <returns>Ввод, который будет передан в симуляцию.</returns>
    public PlayerInput Update(GameWorld world, float deltaSeconds)
    {
        var bot = world.ActivePlane(1);
        var targetPlane = world.ActivePlane(0);
        var targetPilot = world.Pilot(0);

        if (bot is null || !bot.IsAlive || bot.IsEntering || world.Status == MatchStatus.Finished)
        {
            _chargeHeld = false;
            return default;
        }

        var pilotIsTarget = targetPlane is null
            && targetPilot.State is (PilotState.Falling or PilotState.Parachuting or PilotState.OnGround);

        if (targetPlane is null && !pilotIsTarget)
        {
            _chargeHeld = false;
            return default;
        }

        _decisionTimer -= deltaSeconds;

        if (_decisionTimer > 0f)
        {
            return _cachedInput;
        }

        _decisionTimer = 0.06f + ((float)_random.NextDouble() * 0.08f);

        // Лётчик вне аппарата — такая же цель, как и сам аппарат.
        var targetPosition = targetPlane?.Position ?? targetPilot.Position;
        var targetVelocity = targetPlane?.Velocity ?? targetPilot.Velocity;
        var canShoot = targetPlane is null || !targetPlane.IsProtected;

        _cachedInput = bot.Kind == PlaneKind.Biplane
            ? UpdateBiplane(bot, targetPosition, targetVelocity, canShoot)
            : UpdatePterodactyl(bot, targetPosition, targetVelocity, canShoot);

        return _cachedInput;
    }

    private PlayerInput UpdateBiplane(
        Plane bot,
        Vector2 targetPosition,
        Vector2 targetVelocity,
        bool canShoot)
    {
        const float leadTime = 0.12f;
        var predicted = targetPosition + (targetVelocity * leadTime);
        var steering = predicted - bot.Position;
        steering += GetEdgeAvoidance(bot.Position);
        steering += GetGroundAvoidance(bot.Position);

        var distance = Vector2.Distance(bot.Position, targetPosition);
        var desiredAngle = MathF.Atan2(steering.Y, steering.X);
        desiredAngle += ((float)_random.NextDouble() - 0.5f) * 0.07f;

        var angleDelta = GameMath.AngleDelta(bot.Rotation, desiredAngle);
        var turn = GameMath.Clamp(angleDelta * 2.3f, -1f, 1f);
        var thrust = MathF.Abs(angleDelta) > 1.7f ? 0.35f : 1f;
        var fire = canShoot
            && !bot.IsProtected
            && !bot.IsReloading
            && bot.Ammo > 0
            && MathF.Abs(angleDelta) < 0.2f
            && distance < 760f;
        var reload = !bot.IsReloading
            && (bot.Ammo == 0 || (bot.Ammo <= 2 && distance > 420f && MathF.Abs(angleDelta) > 0.5f));
        var useBoost = distance > 680f && bot.BoostCooldownRemaining <= 0f && !bot.IsProtected;

        return new PlayerInput
        {
            Thrust = thrust,
            Turn = turn,
            Fire = fire,
            Reload = reload,
            Ability = useBoost,
            AimX = predicted.X,
            AimY = predicted.Y
        };
    }

    private PlayerInput UpdatePterodactyl(
        Plane bot,
        Vector2 targetPosition,
        Vector2 targetVelocity,
        bool canShoot)
    {
        var distance = Vector2.Distance(bot.Position, targetPosition);
        var leadTime = GameMath.Clamp(distance / _balance.Pterodactyl.MinSpearSpeed, 0.15f, 1.1f);
        var predicted = targetPosition + (targetVelocity * leadTime);
        var aimJitter = ((float)_random.NextDouble() - 0.5f) * 0.09f;
        var aim = predicted + (new Vector2(MathF.Cos(aimJitter), MathF.Sin(aimJitter)) * distance * 0.08f);

        var toTarget = predicted - bot.Position;
        Vector2 move;

        if (distance > 430f)
        {
            move = GameMath.NormalizeOrZero(toTarget);
        }
        else if (distance < 230f)
        {
            move = GameMath.NormalizeOrZero(bot.Position - targetPosition);
        }
        else
        {
            move = GameMath.NormalizeOrZero(new Vector2(-toTarget.Y, toTarget.X)) * 0.55f;
        }

        move += GetEdgeAvoidance(bot.Position) * 0.004f;
        move += GetGroundAvoidance(bot.Position) * 0.004f;
        move = GameMath.NormalizeOrZero(move);

        var fire = false;

        if (canShoot && !bot.IsProtected && bot.ReloadRemaining <= 0f)
        {
            if (!_chargeHeld && distance < 1000f)
            {
                _chargeHeld = true;
            }

            if (_chargeHeld)
            {
                fire = bot.Charge < 0.85f && distance > 240f;

                if (!fire)
                {
                    _chargeHeld = false;
                }
            }
        }
        else
        {
            _chargeHeld = false;
        }

        var dash = distance < 240f && bot.DashCooldownRemaining <= 0f && _random.NextDouble() < 0.08;

        return new PlayerInput
        {
            MoveX = move.X,
            MoveY = move.Y,
            AimX = aim.X,
            AimY = aim.Y,
            Fire = fire,
            Ability = dash
        };
    }

    private static Vector2 GetEdgeAvoidance(Vector2 position)
    {
        const float edgeDistance = 150f;
        const float strength = 260f;
        var result = Vector2.Zero;

        if (position.X < edgeDistance)
        {
            result.X += strength;
        }
        else if (position.X > GameBalance.WorldWidth - edgeDistance)
        {
            result.X -= strength;
        }

        if (position.Y < edgeDistance)
        {
            result.Y += strength;
        }

        return result;
    }

    private static Vector2 GetGroundAvoidance(Vector2 position)
    {
        var danger = GameBalance.GroundY - 170f;

        if (position.Y <= danger)
        {
            return Vector2.Zero;
        }

        var strength = GameMath.Lerp(120f, 620f, (position.Y - danger) / 170f);
        return new Vector2(0f, -strength);
    }
}
