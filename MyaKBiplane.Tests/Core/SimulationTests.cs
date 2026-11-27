using System.Numerics;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Math;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;

namespace MyaKBiplane.Tests.Core;

/// <summary>
/// Подготовка миров для точечных проверок: завершает анимацию появления,
/// чтобы тесты работали с управляемым аппаратом.
/// </summary>
internal static class TestSetup
{
    /// <summary>
    /// Завершает выезд аппарата и, если нужно, снимает защиту.
    /// </summary>
    /// <param name="world">Мир матча.</param>
    /// <param name="slot">Слот аппарата.</param>
    /// <param name="clearProtection">Снять ли защитный период.</param>
    /// <returns>Готовый к управлению аппарат.</returns>
    public static MyaKBiplane.Core.Simulation.Plane ReadyPlane(GameWorld world, int slot, bool clearProtection = true)
    {
        var plane = world.ActivePlane(slot)!;
        plane.EntryRemaining = 0f;
        plane.Position = new Vector2(
            GameBalance.TowerX + (slot == 0 ? -GameBalance.SpawnOffsetX : GameBalance.SpawnOffsetX),
            GameBalance.SpawnY);
        plane.Velocity = Vector2.Zero;

        if (clearProtection)
        {
            plane.ProtectionRemaining = 0f;
        }

        return plane;
    }
}

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
        var player = TestSetup.ReadyPlane(world, 0);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;
        player.Rotation = 0f;

        world.Step(new PlayerInput { Thrust = 1f }, default, 1f / 60f);

        Assert.True(player.Position.X > 400f);
        Assert.True(player.Velocity.X > 0f);
    }

    /// <summary>
    /// Гравитация должна тянуть биплан вниз при отсутствии ввода.
    /// </summary>
    [Fact]
    public void Gravity_PullsPlaneDown()
    {
        var world = new GameWorld(seed: 1);
        world.Reset(PlaneKind.Biplane);
        var player = TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;

        for (var i = 0; i < 60; i++)
        {
            world.Step(default, default, 1f / 60f);
        }

        Assert.NotNull(world.ActivePlane(0));
        Assert.True(player.Position.Y > 450f);
        Assert.True(player.Velocity.Y > 0f);
    }

    /// <summary>
    /// Повторное удержание способности не должно перезапускать ускорение.
    /// </summary>
    [Fact]
    public void Boost_DoesNotRestartWhileHeld()
    {
        var world = CreateWorld();
        var player = TestSetup.ReadyPlane(world, 0);
        world.Step(new PlayerInput { Ability = true }, default, 1f / 60f);

        var boostAfterFirstPress = player.BoostTimeRemaining;
        Assert.True(boostAfterFirstPress > 0f);
        Assert.True(player.BoostCooldownRemaining > 0f);

        world.Step(new PlayerInput { Ability = true }, default, 1f / 60f);

        Assert.True(player.BoostTimeRemaining < boostAfterFirstPress);
    }

    /// <summary>
    /// После кулдауна ускорение снова доступно на том же аппарате.
    /// </summary>
    [Fact]
    public void Boost_BecomesAvailableAgainAfterCooldown()
    {
        var world = CreateWorld();
        var player = TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(800f, 450f);
        player.Velocity = Vector2.Zero;

        world.Step(new PlayerInput { Ability = true }, default, 1f / 60f);
        Assert.True(player.IsBoosting);

        world.Step(default, default, 1f / 60f);

        for (var i = 0; i < 1200 && player.BoostCooldownRemaining > 0f; i++)
        {
            world.Step(default, default, 1f / 60f);
        }

        Assert.False(player.IsBoosting);
        Assert.True(player.BoostCooldownRemaining <= 0f);

        world.Step(default, default, 1f / 60f);
        world.Step(new PlayerInput { Ability = true }, default, 1f / 60f);

        Assert.True(player.IsBoosting);
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
/// Проверки защитного периода и правил столкновения с землёй.
/// </summary>
public sealed class SpawnProtectionTests
{
    /// <summary>
    /// После спавна аппарат неуязвим и не может стрелять.
    /// </summary>
    [Fact]
    public void SpawnProtection_BlocksDamageAndFire()
    {
        var world = new GameWorld(seed: 20);
        var player = world.ActivePlane(0)!;
        var bot = world.ActivePlane(1)!;
        player.EntryRemaining = 0f;

        Assert.True(player.ProtectionRemaining > 0f);
        Assert.Equal(PlaneKind.Biplane, player.Kind);

        world.ApplyDamage(bot, 0, 50f, bot.Position);
        Assert.Equal(GameBalance.MaxHealth, bot.Health, 2);

        for (var i = 0; i < 10; i++)
        {
            world.Step(new PlayerInput { Fire = true }, default, 1f / 60f);
        }

        Assert.Equal(world.Balance.Biplane.MagazineSize, world.ActivePlane(0)!.Ammo);
        Assert.Empty(world.Projectiles);
    }

    /// <summary>
    /// Анимация появления неуправляема, длится одинаково для обоих классов
    /// и оставляет стандартную защиту после передачи управления.
    /// </summary>
    [Fact]
    public void SpawnEntry_PlaysBeforeControlAndKeepsProtection()
    {
        var world = new GameWorld(seed: 38);
        var player = world.ActivePlane(0)!;
        var bot = world.ActivePlane(1)!;
        var startX = player.Position.X;

        Assert.True(player.IsEntering);
        Assert.True(bot.IsEntering);
        Assert.True(
            player.ProtectionRemaining
            >= GameBalance.SpawnEntryDuration + GameBalance.SpawnProtectionDuration);
        Assert.True(startX < GameBalance.TowerX - GameBalance.SpawnOffsetX);

        world.Step(new PlayerInput { Thrust = 1f }, default, 1f / 60f);

        Assert.True(player.IsEntering);
        Assert.True(player.Position.X > startX);

        world.ApplyDamage(player, 1, GameBalance.MaxHealth, player.Position);
        Assert.Equal(GameBalance.MaxHealth, player.Health, 2);

        var steps = 0;

        while (player.IsEntering && steps < 240)
        {
            world.Step(default, default, 1f / 60f);
            steps++;
        }

        Assert.InRange(steps, 141, 147);
        Assert.False(player.IsEntering);
        Assert.False(bot.IsEntering);
        Assert.True(player.ProtectionRemaining > GameBalance.SpawnProtectionDuration - 0.1f);
        Assert.Equal(GameBalance.SpawnY, player.Position.Y, 1);
        Assert.Equal(GameBalance.SpawnEntrySpeed, player.Velocity.Length(), 1);
        Assert.True(player.Rotation < 0f);
        Assert.True(bot.Rotation > MathF.PI);
    }

    /// <summary>
    /// Взлёт биплана плавный: аппарат сначала катится по земле,
    /// а затем без рывка набирает высоту и выходит на курсовую скорость.
    /// </summary>
    [Fact]
    public void SpawnEntry_BiplaneTakeoffIsGradual()
    {
        var world = new GameWorld(seed: 39);
        var plane = world.ActivePlane(0)!;
        var previous = plane.Position;
        var maxStep = 0f;
        var steps = 0;

        while (plane.IsEntering && steps < 240)
        {
            world.Step(default, default, 1f / 60f);
            maxStep = MathF.Max(maxStep, Vector2.Distance(previous, plane.Position));
            previous = plane.Position;
            steps++;

            if (steps == 36)
            {
                // Через 0,6 секунды аппарат ещё катится у земли, а не взмывает вверх.
                Assert.True(plane.Position.Y > GameBalance.SpawnY + 120f);
            }
        }

        Assert.True(steps > 120);
        Assert.True(maxStep < 12f);
        Assert.Equal(GameBalance.SpawnEntrySpeed, plane.Velocity.Length(), 1);
    }

    /// <summary>
    /// Во время защитного периода касание земли не уничтожает аппарат.
    /// </summary>
    [Fact]
    public void SpawnProtection_PreventsGroundCrash()
    {
        var world = new GameWorld(seed: 21);
        var plane = world.ActivePlane(0)!;
        plane.EntryRemaining = 0f;
        Assert.True(plane.IsProtected);

        plane.Position = new Vector2(400f, GameBalance.GroundY - plane.Radius + 1f);
        plane.Velocity = Vector2.Zero;

        world.Step(default, default, 1f / 60f);

        Assert.NotNull(world.ActivePlane(0));
        Assert.True(plane.IsAlive);
        Assert.True(plane.Position.Y + plane.Radius <= GameBalance.GroundY + 0.01f);
    }

    /// <summary>
    /// Без защиты касание земли уничтожает аппарат и приносит очко сопернику.
    /// </summary>
    [Fact]
    public void GroundContact_CrashesPlaneAndAwardsOpponent()
    {
        var world = new GameWorld(seed: 22);
        var plane = TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        plane.Position = new Vector2(400f, GameBalance.GroundY - plane.Radius + 1f);
        plane.Velocity = Vector2.Zero;

        world.Step(default, default, 1f / 60f);

        Assert.Null(world.ActivePlane(0));
        Assert.Equal(0, world.Score(0));
        Assert.Equal(1, world.Score(1));
        Assert.Equal(PilotState.WaitingRespawn, world.Pilot(0).State);
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.Crash);
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
        var player = TestSetup.ReadyPlane(world, 0);
        var bot = TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;
        player.Rotation = 0f;
        bot.Position = new Vector2(700f, 450f);
        bot.Velocity = Vector2.Zero;

        for (var i = 0; i < 30; i++)
        {
            world.Step(new PlayerInput { Fire = true }, default, 1f / 60f);
        }

        Assert.Equal(75f, bot.Health, 2);
        Assert.Equal(0, world.Score(0));
        Assert.Equal(4, player.Ammo);
        Assert.True(bot.IsAlive);
    }

    /// <summary>
    /// Четыре попадания должны уничтожать цель и начислять очко.
    /// </summary>
    [Fact]
    public void FourHits_UnlockScore()
    {
        var world = CreateWorld();
        var player = TestSetup.ReadyPlane(world, 0);
        var bot = TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;
        player.Rotation = 0f;
        bot.Position = new Vector2(700f, 450f);
        bot.Velocity = Vector2.Zero;

        for (var i = 0; i < 170; i++)
        {
            world.Step(new PlayerInput { Fire = true }, default, 1f / 60f);
        }

        Assert.Equal(1, world.Score(0));
        Assert.False(bot.IsAlive);
        Assert.Null(world.ActivePlane(1));
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
        var player = TestSetup.ReadyPlane(world, 0);
        var bot = TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;
        bot.Position = new Vector2(1300f, 450f);
        bot.Velocity = Vector2.Zero;

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

        Assert.True(player.Charge > 0.95f);

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
/// Проверки магазина и ручной перезарядки биплана.
/// </summary>
public sealed class ReloadTests
{
    /// <summary>
    /// Магазин биплана пустеет через пять выстрелов, после чего стрельба блокируется.
    /// </summary>
    [Fact]
    public void Biplane_MagazineEmpties_AndBlocksFire()
    {
        var balance = new GameBalance
        {
            Biplane = new BiplaneBalance { Gravity = 0f }
        };
        var world = new GameWorld(balance, seed: 23);
        world.Reset(PlaneKind.Biplane);
        var player = TestSetup.ReadyPlane(world, 0);
        var target = TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;
        target.Position = new Vector2(400f, 100f);
        target.Velocity = Vector2.Zero;

        var fire = new PlayerInput { Fire = true };

        for (var i = 0; i < 240 && player.Ammo > 0; i++)
        {
            world.Step(fire, default, 1f / 60f);
        }

        Assert.Equal(0, player.Ammo);
        Assert.Equal(world.Balance.Biplane.MagazineSize, player.MagazineSize);

        for (var i = 0; i < 60; i++)
        {
            world.Step(fire, default, 1f / 60f);
        }

        Assert.Equal(0, player.Ammo);
        Assert.False(player.IsReloading);
    }

    /// <summary>
    /// Ручная перезарядка длится 5 секунд, запрещает огонь и возвращает полный магазин.
    /// </summary>
    [Fact]
    public void Biplane_ManualReload_RestoresMagazine()
    {
        var balance = new GameBalance
        {
            Biplane = new BiplaneBalance { Gravity = 0f }
        };
        var world = new GameWorld(balance, seed: 24);
        world.Reset(PlaneKind.Biplane);
        var player = TestSetup.ReadyPlane(world, 0);
        var target = TestSetup.ReadyPlane(world, 1);
        player.Position = new Vector2(400f, 450f);
        player.Velocity = Vector2.Zero;
        target.Position = new Vector2(400f, 100f);
        target.Velocity = Vector2.Zero;
        player.Ammo = 0;

        world.Step(new PlayerInput { Reload = true }, default, 1f / 60f);

        Assert.True(player.IsReloading);
        Assert.Equal(world.Balance.Biplane.ReloadDuration, player.ReloadRemaining, 2);
        Assert.Equal(
            world.Balance.Pterodactyl.MaxReload * 2.5f,
            world.Balance.Biplane.ReloadDuration,
            3);

        for (var i = 0; i < 30; i++)
        {
            world.Step(new PlayerInput { Fire = true }, default, 1f / 60f);
        }

        Assert.Equal(0, player.Ammo);
        Assert.True(player.IsReloading);

        var reloadInput = new PlayerInput { Reload = true };

        for (var i = 0; i < 330; i++)
        {
            world.Step(reloadInput, default, 1f / 60f);
        }

        Assert.False(player.IsReloading);
        Assert.Equal(player.MagazineSize, player.Ammo);
        Assert.Equal(5f, world.Balance.Biplane.ReloadDuration, 3);
    }
}

/// <summary>
/// Проверки катапультирования, парашюта, возврата в аппарат и башни-маяка.
/// </summary>
public sealed class PilotTests
{
    /// <summary>
    /// Катапультирование выбрасывает лётчика вверх без парашюта, аппарат остаётся бесхозным.
    /// </summary>
    [Fact]
    public void Eject_CatapultsFallingPilot()
    {
        var world = new GameWorld(seed: 25);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        var plane = TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        var planeY = plane.Position.Y;

        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        Assert.Null(world.ActivePlane(0));
        Assert.True(plane.IsAbandoned);

        var pilot = world.Pilot(0);
        Assert.Equal(PilotState.Falling, pilot.State);
        Assert.True(pilot.Position.Y < planeY);
        Assert.True(pilot.Velocity.Y < 0f);
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.Eject);

        pilot.Position = new Vector2(500f, 200f);
        pilot.Velocity = new Vector2(0f, 200f);
        world.Step(new PlayerInput { Parachute = true }, default, 1f / 60f);

        Assert.Equal(PilotState.Parachuting, pilot.State);
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.ParachuteOpened);

        for (var i = 0; i < 900 && pilot.State == PilotState.Parachuting; i++)
        {
            world.Step(default, default, 1f / 60f);
        }

        Assert.Equal(PilotState.OnGround, pilot.State);
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.Landed);
    }

    /// <summary>
    /// Пробел повторно складывает уже раскрытый парашют.
    /// </summary>
    [Fact]
    public void Parachute_TogglesOpenAndClosed()
    {
        var world = new GameWorld(seed: 32);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);
        var pilot = world.Pilot(0);

        world.Step(new PlayerInput { Parachute = true }, default, 1f / 60f);
        Assert.Equal(PilotState.Parachuting, pilot.State);

        world.Step(default, default, 1f / 60f);
        world.Step(new PlayerInput { Parachute = true }, default, 1f / 60f);

        Assert.Equal(PilotState.Falling, pilot.State);
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.ParachuteClosed);
    }

    /// <summary>
    /// Попадание в купол уничтожает парашют, но не убивает лётчика.
    /// </summary>
    [Fact]
    public void ParachuteHit_DestroysCanopyWithoutKillingPilot()
    {
        var world = new GameWorld(seed: 38);
        world.Reset(PlaneKind.Pterodactyl, PlaneKind.Biplane, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        var shooter = TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.Parachuting;
        pilot.Position = new Vector2(500f, 400f);
        pilot.Velocity = new Vector2(0f, GameBalance.PilotParachuteFallSpeed);

        // Уводим бесхозный аппарат, чтобы пуля не попала в него раньше купола.
        var abandoned = Assert.Single(
            world.Planes,
            candidate => candidate.Slot == 0 && candidate.IsAbandoned);
        abandoned.Position = new Vector2(1200f, 200f);
        abandoned.Velocity = Vector2.Zero;

        var canopyY = pilot.Position.Y - GameBalance.PilotParachuteCanopyOffsetY;
        shooter.Position = new Vector2(564f, canopyY);
        shooter.Velocity = Vector2.Zero;
        shooter.Rotation = MathF.PI;

        for (var i = 0; i < 10 && !pilot.IsParachuteBroken; i++)
        {
            world.Step(default, new PlayerInput { Fire = true }, 1f / 60f);
        }

        Assert.True(pilot.IsParachuteBroken);
        Assert.Equal(PilotState.Falling, pilot.State);
        Assert.Equal(0, world.Score(0));
        Assert.Equal(0, world.Score(1));
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.ParachuteDestroyed);

        world.Step(new PlayerInput { Parachute = true }, default, 1f / 60f);

        Assert.Equal(PilotState.Falling, pilot.State);
        Assert.DoesNotContain(world.Events, gameEvent => gameEvent.Type == GameEventType.ParachuteOpened);
    }

    /// <summary>
    /// Уничтоженный парашют остаётся недоступным до возрождения лётчика.
    /// </summary>
    [Fact]
    public void BrokenParachute_CannotOpenUntilRespawn()
    {
        var world = new GameWorld(seed: 39);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.Falling;
        pilot.IsParachuteBroken = true;
        pilot.Position = new Vector2(500f, 300f);
        pilot.Velocity = Vector2.Zero;

        world.Step(new PlayerInput { Parachute = true }, default, 1f / 60f);

        Assert.Equal(PilotState.Falling, pilot.State);
        Assert.DoesNotContain(world.Events, gameEvent => gameEvent.Type == GameEventType.ParachuteOpened);

        pilot.State = PilotState.WaitingRespawn;
        pilot.RespawnRemaining = 0f;
        world.Step(default, default, 1f / 60f);

        Assert.Equal(PilotState.InPlane, pilot.State);
        Assert.False(pilot.IsParachuteBroken);
    }

    /// <summary>
    /// Слишком быстрое падение разбивает лётчика и приносит очко сопернику.
    /// </summary>
    [Fact]
    public void UnsafeLanding_KillsPilotAndScoresForOpponent()
    {
        var world = new GameWorld(seed: 33);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.Falling;
        pilot.Position = new Vector2(500f, GameBalance.GroundY - GameBalance.PilotRadius - 2f);
        pilot.Velocity = new Vector2(0f, 600f);

        world.Step(default, default, 1f / 60f);

        Assert.Equal(PilotState.WaitingRespawn, pilot.State);
        Assert.Equal(0, world.Score(0));
        Assert.Equal(1, world.Score(1));
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.PilotKilled);
    }

    /// <summary>
    /// Планирование гасит падение до безопасной скорости, и лётчик выживает.
    /// </summary>
    [Fact]
    public void Gliding_LandsPilotSafely()
    {
        var world = new GameWorld(seed: 34);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.Falling;
        pilot.Position = new Vector2(500f, GameBalance.GroundY - GameBalance.PilotRadius - 2f);
        pilot.Velocity = new Vector2(0f, 600f);

        world.Step(new PlayerInput { MoveX = 1f }, default, 1f / 60f);

        Assert.Equal(PilotState.OnGround, pilot.State);
        Assert.True(pilot.Velocity.Y <= GameBalance.PilotSafeLandingSpeed);
    }

    /// <summary>
    /// Снаряд соперника убивает свободного лётчика и приносит очко стрелку.
    /// </summary>
    [Fact]
    public void Projectile_KillsFreePilot_AndScores()
    {
        var world = new GameWorld(seed: 35);
        world.Reset(PlaneKind.Pterodactyl, PlaneKind.Biplane, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        var shooter = TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.Falling;
        pilot.Position = new Vector2(500f, 400f);
        pilot.Velocity = Vector2.Zero;

        // Уводим бесхозный аппарат, чтобы пуля не попала в него раньше лётчика.
        var abandoned = Assert.Single(
            world.Planes,
            candidate => candidate.Slot == 0 && candidate.IsAbandoned);
        abandoned.Position = new Vector2(1200f, 200f);
        abandoned.Velocity = Vector2.Zero;

        shooter.Position = new Vector2(564f, 400f);
        shooter.Velocity = Vector2.Zero;
        shooter.Rotation = MathF.PI;

        for (var i = 0; i < 6 && pilot.State == PilotState.Falling; i++)
        {
            world.Step(default, new PlayerInput { Fire = true }, 1f / 60f);
        }

        Assert.Equal(PilotState.WaitingRespawn, pilot.State);
        Assert.Equal(1, world.Score(1));
        Assert.Equal(0, world.Score(0));
    }

    /// <summary>
    /// Повторное нажатие E рядом с бесхозным аппаратом возвращает лётчика в кабину.
    /// </summary>
    [Fact]
    public void Eject_ThenReenter_ReturnsPilotToPlane()
    {
        var world = new GameWorld(seed: 26);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);

        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);
        var abandoned = Assert.Single(world.Planes, candidate => candidate.Slot == 0 && candidate.IsAbandoned);

        world.Step(default, default, 1f / 60f);

        var pilot = world.Pilot(0);
        abandoned.Position = pilot.Position;

        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        Assert.Equal(PilotState.InPlane, pilot.State);
        Assert.False(abandoned.IsAbandoned);
        Assert.Same(abandoned, world.ActivePlane(0));
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.Reenter);
    }

    /// <summary>
    /// Добравшийся до башни лётчик получает новый аппарат с полным здоровьем и защитой.
    /// </summary>
    [Fact]
    public void PilotAtTower_GetsNewProtectedPlane()
    {
        var world = new GameWorld(seed: 27);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);

        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var abandoned = Assert.Single(world.Planes, candidate => candidate.Slot == 0 && candidate.IsAbandoned);
        abandoned.Position = new Vector2(120f, 300f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.OnGround;
        pilot.Position = new Vector2(GameBalance.TowerX, GameBalance.GroundY - GameBalance.PilotRadius);
        pilot.Velocity = Vector2.Zero;

        world.Step(default, default, 1f / 60f);

        var respawned = world.ActivePlane(0);
        Assert.NotNull(respawned);
        Assert.NotEqual(abandoned.Id, respawned!.Id);
        Assert.Equal(PlaneKind.Biplane, respawned.Kind);
        Assert.Equal(GameBalance.MaxHealth, respawned.Health, 2);
        Assert.True(respawned.IsEntering);
        Assert.True(
            respawned.ProtectionRemaining
            > GameBalance.SpawnProtectionDuration + (GameBalance.SpawnEntryDuration * 0.9f));
        Assert.Equal(GameBalance.SpawnEntryInset, respawned.Position.X, 2);
        Assert.Contains(world.Events, gameEvent => gameEvent.Type == GameEventType.TowerReached);
    }

    /// <summary>
    /// Одинаковые классы аппаратов допустимы в сетевом матче без бота.
    /// </summary>
    [Fact]
    public void Reset_AllowsSameClassForBothSlots()
    {
        var world = new GameWorld(seed: 28);
        world.Reset(PlaneKind.Biplane, PlaneKind.Biplane, secondIsBot: false);

        Assert.Equal(PlaneKind.Biplane, world.ActivePlane(0)!.Kind);
        Assert.Equal(PlaneKind.Biplane, world.ActivePlane(1)!.Kind);
        Assert.False(world.ActivePlane(1)!.IsBot);
        Assert.False(world.Pilot(1).IsBot);
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
        var player = TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        player.Velocity = Vector2.Zero;

        world.Step(
            new PlayerInput { Ability = true, MoveX = 1f, AimX = 800f, AimY = 450f },
            default,
            1f / 60f);

        Assert.True(player.Velocity.Length() > 0f);
        Assert.True(player.DashCooldownRemaining > 2.4f);

        for (var i = 0; i < 60; i++)
        {
            world.Step(default, default, 1f / 60f);
        }

        var cooldownAfterSecond = player.DashCooldownRemaining;
        world.Step(
            new PlayerInput { Ability = true, MoveX = 1f, AimX = 800f, AimY = 450f },
            default,
            1f / 60f);

        Assert.True(player.DashCooldownRemaining <= cooldownAfterSecond);
    }
}

/// <summary>
/// Проверки бота, снимков и детерминизма.
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
        TestSetup.ReadyPlane(world, 0);
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
    /// Бот на биплане с пустым магазином должен запросить перезарядку.
    /// </summary>
    [Fact]
    public void BotBiplane_ReloadsWhenEmpty()
    {
        var world = new GameWorld(seed: 29);
        world.Reset(PlaneKind.Pterodactyl, PlaneKind.Biplane, secondIsBot: true);
        TestSetup.ReadyPlane(world, 0);
        var botPlane = TestSetup.ReadyPlane(world, 1);
        botPlane.Ammo = 0;

        var bot = new BotController(seed: 30, world.Balance);
        var input = bot.Update(world, 1f / 60f);

        Assert.True(input.Reload);
    }

    /// <summary>
    /// Бот должен считать свободного лётчика полноценной целью.
    /// </summary>
    [Fact]
    public void Bot_TargetsFreePilot()
    {
        var world = new GameWorld(seed: 36);
        world.Reset(PlaneKind.Pterodactyl, PlaneKind.Biplane, secondIsBot: true);
        TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.State = PilotState.Falling;
        pilot.Position = new Vector2(760f, 320f);
        pilot.Velocity = Vector2.Zero;

        var bot = new BotController(seed: 37, world.Balance);
        var input = bot.Update(world, 1f / 60f);

        Assert.InRange(input.AimX, pilot.Position.X - 1f, pilot.Position.X + 1f);
        Assert.InRange(input.AimY, pilot.Position.Y - 1f, pilot.Position.Y + 1f);
    }

    /// <summary>
    /// Десять уничтожений завершают матч и определяют победителя.
    /// </summary>
    [Fact]
    public void TenKills_FinishMatch_AndSetWinner()
    {
        var world = new GameWorld(seed: 31);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);

        for (var kill = 0; kill < GameBalance.ScoreToWin; kill++)
        {
            var target = world.ActivePlane(1);
            Assert.NotNull(target);
            target!.ProtectionRemaining = 0f;
            world.ApplyDamage(target, 0, GameBalance.MaxHealth, target.Position);
            Assert.Equal(kill + 1, world.Score(0));

            if (world.Status == MatchStatus.Finished)
            {
                break;
            }

            for (var step = 0; step < 180 && world.ActivePlane(1) is null; step++)
            {
                world.Step(default, default, 1f / 60f);
            }
        }

        Assert.Equal(MatchStatus.Finished, world.Status);
        Assert.Equal(0, world.WinnerSlot);
        Assert.Equal(GameBalance.ScoreToWin, world.Score(0));
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

        Assert.Equal(2, snapshot.Planes.Length);
        Assert.Equal(2, snapshot.Pilots.Length);
        Assert.Equal(GameBalance.WorldWidth, snapshot.WorldWidth);
        Assert.Equal(GameBalance.WorldHeight, snapshot.WorldHeight);
        Assert.Equal(GameBalance.TowerX, snapshot.Tower.X);
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
        var firstPlane = first.ActivePlane(0)!;
        var secondPlane = second.ActivePlane(0)!;

        var input = new PlayerInput { Thrust = 1f, Turn = 0.35f, Fire = true };

        for (var i = 0; i < 120; i++)
        {
            first.Step(input, default, 1f / 60f);
            second.Step(input, default, 1f / 60f);
        }

        Assert.Equal(firstPlane.Position.X, secondPlane.Position.X, 3);
        Assert.Equal(firstPlane.Position.Y, secondPlane.Position.Y, 3);
        Assert.Equal(firstPlane.Rotation, secondPlane.Rotation, 3);
        Assert.Equal(first.Score(0), second.Score(0));
        Assert.Equal(first.Score(1), second.Score(1));
        Assert.Equal(first.Status, second.Status);
    }
}
