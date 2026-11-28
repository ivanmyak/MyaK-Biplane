using System.Numerics;
using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Simulation;

namespace MyaKBiplane.Tests.Core;

/// <summary>
/// Проверки правил, позаимствованных у BT Biplanes: тяга от тангажа,
/// просадка на малой скорости, перпендикулярное катапультирование и толчок купола.
/// </summary>
public sealed class OriginalMathTests
{
    private static GameWorld CreateWorld(float gravity = 0f) => new(new GameBalance
    {
        Biplane = new BiplaneBalance
        {
            Gravity = gravity,
            MaxStallSink = gravity > 0f ? 260f : 0f,
            ClimbDrag = gravity > 0f ? 0.35f : 0f
        }
    });

    /// <summary>
    /// Горизонтальный полёт разгоняется заметно быстрее, чем свеча.
    /// </summary>
    [Fact]
    public void Thrust_IsStrongerInLevelFlightThanInClimb()
    {
        var level = CreateWorld();
        level.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        var levelPlane = TestSetup.ReadyPlane(level, 0);
        levelPlane.Rotation = 0f;

        var climb = CreateWorld();
        climb.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        var climbPlane = TestSetup.ReadyPlane(climb, 0);
        climbPlane.Rotation = -MathF.PI / 2f;

        level.Step(new PlayerInput { Thrust = 1f }, default, 1f / 60f);
        climb.Step(new PlayerInput { Thrust = 1f }, default, 1f / 60f);

        Assert.True(levelPlane.Velocity.Length() > climbPlane.Velocity.Length() * 3f);
    }

    /// <summary>
    /// Медленный аппарат проседает даже при выключенной гравитации.
    /// </summary>
    [Fact]
    public void SlowBiplane_Sinks()
    {
        var world = CreateWorld(gravity: 170f);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        var plane = TestSetup.ReadyPlane(world, 0);
        plane.Velocity = Vector2.Zero;

        world.Step(default, default, 1f / 60f);

        Assert.True(plane.Velocity.Y > 0f);
    }

    /// <summary>
    /// Катапультирование направлено перпендикулярно носу, как в оригинале.
    /// </summary>
    [Fact]
    public void Ejection_PushesPerpendicularToNose()
    {
        var world = CreateWorld();
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        var plane = TestSetup.ReadyPlane(world, 0);
        plane.Rotation = 0f;
        plane.Velocity = Vector2.Zero;

        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        Assert.Equal(PilotState.Falling, pilot.State);
        Assert.True(pilot.Velocity.Y < -200f);
        Assert.InRange(pilot.Velocity.X, -1f, 1f);
    }

    /// <summary>
    /// Раскрытие купола даёт дополнительный рывок вверх.
    /// </summary>
    [Fact]
    public void Parachute_OpeningGivesUpwardKick()
    {
        var world = CreateWorld();
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        TestSetup.ReadyPlane(world, 0);
        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        var pilot = world.Pilot(0);
        pilot.Position = new Vector2(800f, 200f);
        pilot.Velocity = new Vector2(0f, 200f);

        world.Step(new PlayerInput { Parachute = true }, default, 1f / 60f);

        Assert.Equal(PilotState.Parachuting, pilot.State);
        Assert.True(pilot.Velocity.Y < 200f - GameBalance.PilotParachuteOpenImpulse + 20f);
    }
}
