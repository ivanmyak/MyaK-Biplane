using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;

namespace MyaKBiplane.Tests.Core;

/// <summary>
/// Проверки катапультирования бота и управления покинутым лётчиком.
/// </summary>
public sealed class BotEjectionTests
{
    /// <summary>
    /// Сильно повреждённый бот покидает аппарат, а не ждёт гибели.
    /// </summary>
    [Fact]
    public void Bot_EjectsWhenHeavilyDamaged()
    {
        var world = new GameWorld(seed: 41);
        world.Reset(PlaneKind.Biplane, PlaneKind.Biplane, secondIsBot: true);
        TestSetup.ReadyPlane(world, 0);
        var botPlane = TestSetup.ReadyPlane(world, 1);
        botPlane.Health = 20f;

        var bot = new BotController(seed: 42, world.Balance);

        for (var step = 0; step < 20 && world.Pilot(1).State == PilotState.InPlane; step++)
        {
            var input = bot.Update(world, 1f / 60f);
            world.Step(default, input, 1f / 60f);
        }

        Assert.Equal(PilotState.Falling, world.Pilot(1).State);
        Assert.True(botPlane.IsAbandoned);
        Assert.Contains(world.Events, gameEvent =>
            gameEvent.Type == GameEventType.Eject && gameEvent.Slot == 1);
    }

    /// <summary>
    /// Покинутый бот раскрывает парашют, пока достаточно высоты.
    /// </summary>
    [Fact]
    public void Bot_OpensParachuteAfterEjecting()
    {
        var world = new GameWorld(seed: 43);
        world.Reset(PlaneKind.Biplane, PlaneKind.Biplane, secondIsBot: true);
        TestSetup.ReadyPlane(world, 0);
        var botPlane = TestSetup.ReadyPlane(world, 1);
        botPlane.Health = 0f;

        var pilot = world.Pilot(1);
        pilot.State = PilotState.Falling;
        pilot.Position = new System.Numerics.Vector2(900f, 200f);
        pilot.Velocity = new System.Numerics.Vector2(0f, 120f);

        var bot = new BotController(seed: 44, world.Balance);

        for (var step = 0; step < 20 && pilot.State == PilotState.Falling; step++)
        {
            var input = bot.Update(world, 1f / 60f);
            world.Step(default, input, 1f / 60f);
        }

        Assert.Equal(PilotState.Parachuting, pilot.State);
        Assert.Contains(world.Events, gameEvent =>
            gameEvent.Type == GameEventType.ParachuteOpened && gameEvent.Slot == 1);
    }
}
