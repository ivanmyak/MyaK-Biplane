using System.Numerics;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Math;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;

namespace MyaKBiplane.Tests.Core;

/// <summary>
/// Проверки математических помощников симуляции.
/// </summary>
public sealed class GameMathTests
{
    /// <summary>
    /// Нормализация угла должна возвращать значение в диапазоне -π..π.
    /// </summary>
    [Fact]
    public void NormalizeAngle_WrapsToPiRange()
    {
        Assert.Equal(0f, GameMath.NormalizeAngle(MathF.PI * 2f), 3);
        Assert.Equal(MathF.PI, GameMath.NormalizeAngle(MathF.PI), 3);
        Assert.Equal(-MathF.PI / 2f, GameMath.NormalizeAngle(MathF.PI * 1.5f), 3);
    }

    /// <summary>
    /// Ограничение длины вектора не должно менять короткие векторы.
    /// </summary>
    [Fact]
    public void ClampLength_LeavesShortVectorsUnchanged()
    {
        var value = new Vector2(3f, 4f);

        Assert.Equal(value, GameMath.ClampLength(value, 10f));
        Assert.Equal(1f, GameMath.ClampLength(value, 1f).Length(), 3);
    }
}

/// <summary>
/// Проверки физики биплана.
/// </summary>
public sealed class BiplanePhysicsTests
{
    /// <summary>
    /// Тяга должна разгонять биплан в направлении носа.
    /// </summary>
    [Fact]
    public void Thrust_MovesPlaneForward()
    {
        var world = CreateWorld();
        world.Player.Position = new Vector2(400f, 450f);
        world.Player.Velocity = Vector2.Zero;
        world.Player.Rotation = 0f;

        world.Step(new PlayerInput { Thrust = 1f }, default, 1f / 60f);

        Assert.True(world.Player.Position.X > 400f);
        Assert.True(world.Player.Velocity.X > 0f);
    }

    /// <summary>
    /// Гравитация должна тянуть биплан вниз при отсутствии ввода.
    /// </summary>
    [Fact]
    public void Gravity_PullsPlaneDown()
    {
        var world = new GameWorld(seed: 1);
        world.Reset(PlaneKind.Biplane);
        world.Player.Position = new Vector2(400f, 450f);
        world.Player.Velocity = Vector2.Zero;

        for (var i = 0; i < 60; i++)
        {
            world.Step(default, default, 1f / 60f);
        }

        Assert.True(world.Player.Position.Y > 450f);
        Assert.True(world.Player.Velocity.Y > 0f);
    }

    /// <summary>
    /// Повторное удержание способности не должно перезапускать ускорение.
    /// </summary>
    [Fact]
    public void Boost_DoesNotRestartWhileHeld()
    {
        var world = CreateWorld();
        world.Step(new PlayerInput { Ability = true }, default, 1f / 60f);

        var boostAfterFirstPress = world.Player.BoostTimeRemaining;
        Assert.True(boostAfterFirstPress > 0f);
        Assert.True(world.Player.BoostCooldownRemaining > 0f);

        world.Step(new PlayerInput { Ability = true }, default, 1f / 60f);

        Assert.True(world.Player.BoostTimeRemaining < boostAfterFirstPress);
    }

    private static GameWorld CreateWorld()
    {
        var balance = new GameBalance
        {
            Biplane = new BiplaneBalance { Gravity = 0f }
        };

        var world = new GameWorld(balance, seed: 1);
        world.Reset(PlaneKind.Biplane);
        return world;
    }
}

/// <summary>
/// Проверки стрельбы, урона и вооружения.
/// </summary>
public sealed class CombatTests
{
    /// <summary>
    /// Одна пуля должна снимать четверть здоровья.
    /// </summary>
    [Fact]
    public void Bullet_HitsEnemy_AndDealsQuarterDamage()
    {
        var world = CreateWorld();
        world.Player.Position = new Vector2(400f, 450f);
        world.Player.Velocity = Vector2.Zero;
        world.Player.Rotation = 0f;
        world.Bot.Position = new Vector2(700f, 450f);
        world.Bot.Velocity = Vector2.Zero;

        for (var i = 0; i < 30; i++)
        {
            world.Step(new PlayerInput { Fire = true }, default, 1f / 60f);
        }

        Assert.Equal(75f, world.Bot.Health, 2);
        Assert.Equal(0, world.Player.Score);
        Assert.True(world.Bot.IsAlive);
    }

    /// <summary>
    /// Четыре попадания должны уничтожать цель и начислять очко.
    /// </summary>
    [Fact]
    public void FourHits_UnlockScore()
    {
        var world = CreateWorld();
        world.Player.Position = new Vector2(400f, 450f);
        world.Player.Velocity = Vector2.Zero;
        world.Player.Rotation = 0f;
        world.Bot.Position = new Vector2(700f, 450f);
        world.Bot.Velocity = Vector2.Zero;

        for (var i = 0; i < 170; i++)
        {
            world.Step(new PlayerInput { Fire = true }, default, 1f / 60f);
        }

        Assert.Equal(1, world.Player.Score);
        Assert.False(world.Bot.IsAlive);
    }

    /// <summary>
    /// Полная зарядка должна давать максимальный урон и скорость копья.
    /// </summary>
    [Fact]
    public void Pterodactyl_FullCharge_IncreasesSpearDamage()
    {
        var balance = new GameBalance
        {
            Biplane = new BiplaneBalance { Gravity = 0f }
        };
        var world = new GameWorld(balance, seed: 2);
        world.Reset(PlaneKind.Pterodactyl);
        world.Player.Position = new Vector2(400f, 450f);
        world.Player.Velocity = Vector2.Zero;
        world.Bot.Position = new Vector2(1300f, 450f);
        world.Bot.Velocity = Vector2.Zero;

        var aim = new PlayerInput
        {
            AimX = 1300f,
            AimY = 450f,
            Fire = true
        };

        for (var i = 0; i < 72; i++)
        {
            world.Step(aim, default, 1f / 60f);
        }

        Assert.True(world.Player.Charge > 0.95f);

        world.Step(new PlayerInput { AimX = 1300f, AimY = 450f, Fire = false }, default, 1f / 60f);

        var spear = Assert.Single(world.Projectiles, projectile => projectile.Kind == ProjectileKind.Spear);
        Assert.Equal(balance.Pterodactyl.MaxDamage, spear.Damage, 2);
        Assert.Equal(balance.Pterodactyl.MaxSpearSpeed, spear.Velocity.Length(), 1);
    }

    private static GameWorld CreateWorld()
    {
        var balance = new GameBalance
        {
            Biplane = new BiplaneBalance { Gravity = 0f }
        };

        var world = new GameWorld(balance, seed: 1);
        world.Reset(PlaneKind.Biplane);
        return world;
    }
}

/// <summary>
/// Проверки движения птеродактиля.
/// </summary>
public sealed class PterodactylPhysicsTests
{
    /// <summary>
    /// Рывок должен давать импульс и уходить на кулдаун.
    /// </summary>
    [Fact]
    public void Dash_AppliesImpulse_AndStartsCooldown()
    {
        var world = new GameWorld(seed: 3);
        world.Reset(PlaneKind.Pterodactyl);
        world.Player.Velocity = Vector2.Zero;

        world.Step(
            new PlayerInput { Ability = true, MoveX = 1f, AimX = 800f, AimY = 450f },
            default,
            1f / 60f);

        Assert.True(world.Player.Velocity.Length() > 0f);
        Assert.True(world.Player.DashCooldownRemaining > 2.4f);

        for (var i = 0; i < 60; i++)
        {
            world.Step(default, default, 1f / 60f);
        }

        var cooldownAfterSecond = world.Player.DashCooldownRemaining;
        world.Step(
            new PlayerInput { Ability = true, MoveX = 1f, AimX = 800f, AimY = 450f },
            default,
            1f / 60f);

        Assert.True(world.Player.DashCooldownRemaining <= cooldownAfterSecond);
    }
}

/// <summary>
/// Проверки бота и сессии.
/// </summary>
public sealed class BotAndSessionTests
{
    /// <summary>
    /// Бот должен выдавать конечный ввод в допустимых диапазонах.
    /// </summary>
    [Fact]
    public void Bot_ProducesFiniteInput()
    {
        var world = new GameWorld(seed: 4);
        world.Reset(PlaneKind.Biplane);
        var bot = new BotController(seed: 5);

        var input = bot.Update(world, 1f / 60f);

        Assert.True(float.IsFinite(input.Thrust));
        Assert.True(float.IsFinite(input.Turn));
        Assert.InRange(input.Thrust, -1f, 1f);
        Assert.InRange(input.Turn, -1f, 1f);
        Assert.InRange(input.MoveX, -1f, 1f);
        Assert.InRange(input.MoveY, -1f, 1f);
    }

    /// <summary>
    /// Снимок сессии должен содержать оба аппарата и размеры мира.
    /// </summary>
    [Fact]
    public void Session_Snapshot_ContainsPlanesAndWorldSize()
    {
        var session = new GameSession(PlaneKind.Biplane, seed: 6);

        session.Update(default, 1f / 60f);
        var snapshot = session.CreateSnapshot();

        Assert.Equal(2, snapshot.Planes.Count);
        Assert.Equal(GameBalance.WorldWidth, snapshot.WorldWidth);
        Assert.Equal(GameBalance.WorldHeight, snapshot.WorldHeight);
        Assert.Contains("\"worldWidth\"", session.CreateSnapshotJson());
    }

    /// <summary>
    /// Одинаковые входные данные должны давать одинаковое состояние.
    /// </summary>
    [Fact]
    public void Simulation_IsDeterministic()
    {
        var first = new GameWorld(seed: 7);
        var second = new GameWorld(seed: 7);
        first.Reset(PlaneKind.Biplane);
        second.Reset(PlaneKind.Biplane);

        var input = new PlayerInput { Thrust = 1f, Turn = 0.35f, Fire = true };

        for (var i = 0; i < 120; i++)
        {
            first.Step(input, default, 1f / 60f);
            second.Step(input, default, 1f / 60f);
        }

        Assert.Equal(first.Player.Position.X, second.Player.Position.X, 3);
        Assert.Equal(first.Player.Position.Y, second.Player.Position.Y, 3);
        Assert.Equal(first.Player.Rotation, second.Player.Rotation, 3);
    }
}
