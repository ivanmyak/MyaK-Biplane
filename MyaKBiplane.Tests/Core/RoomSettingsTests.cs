using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Tests.Core;

/// <summary>
/// Проверки пользовательских правил комнаты: лимит уничтожений,
/// одинаковые аппараты и запрет катапультирования.
/// </summary>
public sealed class RoomSettingsTests
{
    /// <summary>
    /// Настройки по умолчанию совпадают со стандартными правилами игры.
    /// </summary>
    [Fact]
    public void Defaults_MatchStandardGame()
    {
        var settings = RoomSettings.Default;

        Assert.True(settings.IsDefault);
        Assert.Equal(GameBalance.ScoreToWin, settings.ScoreToWin);
        Assert.True(settings.AllowSameKind);
        Assert.True(settings.AllowEjection);
    }

    /// <summary>
    /// Недопустимый лимит уничтожений приводится к границам.
    /// </summary>
    [Theory]
    [InlineData(-5, RoomSettings.MinScoreToWin)]
    [InlineData(3, 3)]
    [InlineData(999, RoomSettings.MaxScoreToWin)]
    public void Normalized_ClampsScoreToWin(int requested, int expected)
    {
        var settings = new RoomSettings { ScoreToWin = requested }.Normalized();

        Assert.Equal(expected, settings.ScoreToWin);
    }

    /// <summary>
    /// Запрет одинаковых аппаратов меняет класс только совпавшего выбора.
    /// </summary>
    [Theory]
    [InlineData(true, PlaneKind.Biplane, PlaneKind.Biplane, PlaneKind.Biplane)]
    [InlineData(false, PlaneKind.Biplane, PlaneKind.Biplane, PlaneKind.Pterodactyl)]
    [InlineData(false, PlaneKind.Biplane, PlaneKind.Pterodactyl, PlaneKind.Pterodactyl)]
    [InlineData(false, PlaneKind.Pterodactyl, PlaneKind.Biplane, PlaneKind.Biplane)]
    public void ResolveGuestKind_AppliesSameKindRule(
        bool allowSameKind,
        PlaneKind hostKind,
        PlaneKind requestedKind,
        PlaneKind expectedKind)
    {
        var settings = new RoomSettings { AllowSameKind = allowSameKind };

        Assert.Equal(expectedKind, settings.ResolveGuestKind(hostKind, requestedKind));
    }

    /// <summary>
    /// Своё количество уничтожений завершает матч раньше стандартного лимита.
    /// </summary>
    [Fact]
    public void CustomScoreToWin_FinishesMatchEarly()
    {
        const int scoreToWin = 2;
        var rules = new MatchRules { ScoreToWin = scoreToWin };
        var world = new GameWorld(seed: 31, rules: rules);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);

        for (var kill = 0; kill < scoreToWin; kill++)
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
        Assert.Equal(scoreToWin, world.Score(0));
    }

    /// <summary>
    /// При запрете катапультирования лётчик остаётся в аппарате.
    /// </summary>
    [Fact]
    public void EjectionDisabled_KeepsPilotInPlane()
    {
        var rules = new MatchRules { AllowEjection = false };
        var world = new GameWorld(seed: 25, rules: rules);
        world.Reset(PlaneKind.Biplane, PlaneKind.Pterodactyl, secondIsBot: false);
        var plane = TestSetup.ReadyPlane(world, 0);
        TestSetup.ReadyPlane(world, 1);

        world.Step(new PlayerInput { Eject = true }, default, 1f / 60f);

        Assert.Same(plane, world.ActivePlane(0));
        Assert.False(plane.IsAbandoned);
        Assert.Equal(PilotState.InPlane, world.Pilot(0).State);
        Assert.DoesNotContain(world.Events, gameEvent => gameEvent.Type == GameEventType.Eject);
    }

    /// <summary>
    /// Хост P2P-матча передаёт настройки комнаты в состояние матча.
    /// </summary>
    [Fact]
    public void HostSession_PublishesRoomSettings()
    {
        var settings = new RoomSettings
        {
            ScoreToWin = 4,
            AllowSameKind = false,
            AllowEjection = false
        };
        var session = new HostGameSession(
            PlaneKind.Biplane,
            PlaneKind.Pterodactyl,
            seed: 5,
            settings: settings);

        Assert.Equal(4, session.Settings.ScoreToWin);
        Assert.False(session.Settings.AllowEjection);

        session.Start();
        var hostInfo = session.BuildInfo("ABC123", 0);
        var guestInfo = session.BuildInfo("ABC123", 1);

        Assert.Equal(4, hostInfo.Settings.ScoreToWin);
        Assert.False(hostInfo.Settings.AllowSameKind);
        Assert.False(guestInfo.Settings.AllowEjection);
    }

    /// <summary>
    /// Локальная сессия с ботом тоже уважает пользовательский лимит.
    /// </summary>
    [Fact]
    public void GameSession_AppliesRoomSettings()
    {
        var session = new GameSession(
            PlaneKind.Biplane,
            seed: 6,
            settings: new RoomSettings { ScoreToWin = 3 });

        Assert.Equal(3, session.Settings.ScoreToWin);
    }
}
