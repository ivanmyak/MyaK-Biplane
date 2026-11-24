using System.Text.Json;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Game;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Tests.Core;

/// <summary>
/// Проверки хоста P2P-матча: классы аппаратов, пауза и реванш.
/// </summary>
public sealed class HostGameSessionTests
{
    private static readonly int[] ZeroScores = [0, 0];

    [Theory]
    [InlineData(PlaneKind.Biplane, PlaneKind.Pterodactyl)]
    [InlineData(PlaneKind.Pterodactyl, PlaneKind.Biplane)]
    [InlineData(PlaneKind.Pterodactyl, PlaneKind.Pterodactyl)]
    [InlineData(PlaneKind.Biplane, PlaneKind.Biplane)]
    public void Start_uses_kind_chosen_by_each_player(
        PlaneKind hostKind,
        PlaneKind guestKind)
    {
        var session = new HostGameSession(hostKind, guestKind, seed: 42);
        session.Start();

        using var hostSnapshot = JsonDocument.Parse(session.CreateSnapshotPairJson().Host);
        using var guestSnapshot = JsonDocument.Parse(session.CreateSnapshotPairJson().Guest);

        var hostPlanes = hostSnapshot.RootElement.GetProperty("planes").EnumerateArray().ToArray();
        var guestPlanes = guestSnapshot.RootElement.GetProperty("planes").EnumerateArray().ToArray();

        Assert.Equal(ToName(hostKind), hostPlanes[0].GetProperty("kind").GetString());
        Assert.Equal(ToName(guestKind), hostPlanes[1].GetProperty("kind").GetString());
        Assert.Equal(ToName(hostKind), guestPlanes[0].GetProperty("kind").GetString());
        Assert.Equal(ToName(guestKind), guestPlanes[1].GetProperty("kind").GetString());
        Assert.Equal(1, guestSnapshot.RootElement.GetProperty("yourSlot").GetInt32());
    }

    [Fact]
    public void Pause_stops_simulation_until_resumed()
    {
        var session = new HostGameSession(PlaneKind.Biplane, PlaneKind.Pterodactyl, seed: 7);
        session.Start();
        session.SetInput(0, new InputState { Thrust = 1f, Turn = 1f });

        for (var i = 0; i < 30; i++)
        {
            session.Update(1f / 60f);
        }

        Assert.True(session.TogglePause(slot: 1));
        Assert.Equal("paused", session.Status);
        Assert.Equal(1, session.PausedBySlot);

        using var before = JsonDocument.Parse(session.CreateSnapshotJson(0));
        var beforeX = before.RootElement.GetProperty("planes")[0].GetProperty("x").GetSingle();

        for (var i = 0; i < 30; i++)
        {
            session.Update(1f / 60f);
        }

        using var after = JsonDocument.Parse(session.CreateSnapshotJson(0));
        var afterX = after.RootElement.GetProperty("planes")[0].GetProperty("x").GetSingle();
        Assert.Equal(beforeX, afterX);

        Assert.True(session.TogglePause(slot: 0));
        Assert.Equal("playing", session.Status);
    }

    [Fact]
    public void Rematch_starts_when_both_players_agree()
    {
        var session = new HostGameSession(PlaneKind.Biplane, PlaneKind.Pterodactyl, seed: 11);
        session.Start();

        Assert.True(session.OfferRematch(slot: 1));
        Assert.Equal(1, session.RematchOfferSlot);
        Assert.Equal("playing", session.Status);

        Assert.True(session.OfferRematch(slot: 0));
        Assert.Null(session.RematchOfferSlot);
        Assert.Equal("playing", session.Status);

        using var snapshot = JsonDocument.Parse(session.CreateSnapshotJson(0));
        var scores = snapshot.RootElement.GetProperty("scores").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(ZeroScores, scores);
    }

    private static string ToName(PlaneKind kind) =>
        kind == PlaneKind.Biplane ? "biplane" : "pterodactyl";
}
