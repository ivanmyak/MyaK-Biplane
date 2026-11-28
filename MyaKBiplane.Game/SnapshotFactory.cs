using MyaKBiplane.Core.Balance;
using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Game;

/// <summary>
/// Создаёт снимки состояния мира для рендерера и сети.
/// </summary>
public static class SnapshotFactory
{
    /// <summary>
    /// Формирует снимок мира.
    /// </summary>
    /// <param name="world">Мир матча.</param>
    /// <param name="yourSlot">Слот получателя снимка.</param>
    /// <param name="status">Статус матча: <c>playing</c>, <c>paused</c> или <c>finished</c>.</param>
    /// <param name="events">События, накопленные с предыдущего снимка.</param>
    /// <returns>Снимок для сериализации.</returns>
    public static GameSnapshot Create(
        GameWorld world,
        int yourSlot,
        string status,
        IReadOnlyList<GameEvent>? events = null)
    {
        var planes = new SnapshotPlane[world.Planes.Count];

        for (var i = 0; i < world.Planes.Count; i++)
        {
            var plane = world.Planes[i];
            planes[i] = new SnapshotPlane
            {
                Id = plane.Id,
                Slot = plane.Slot,
                Kind = ToKindName(plane.Kind),
                IsBot = plane.IsBot,
                Abandoned = plane.IsAbandoned,
                Alive = plane.IsAlive,
                X = plane.Position.X,
                Y = plane.Position.Y,
                Angle = plane.Rotation,
                Health = plane.Health,
                MaxHealth = GameBalance.MaxHealth,
                Boost = plane.BoostTimeRemaining,
                BoostCooldown = plane.BoostCooldownRemaining,
                DashCooldown = plane.DashCooldownRemaining,
                AbilityCooldownDuration = plane.Kind == PlaneKind.Biplane
                    ? world.Balance.Biplane.BoostDuration + world.Balance.Biplane.BoostCooldown
                    : world.Balance.Pterodactyl.DashCooldown,
                Charge = plane.Charge,
                Reload = plane.ReloadRemaining,
                ReloadDuration = plane.Kind == PlaneKind.Biplane
                    ? world.Balance.Biplane.ReloadDuration
                    : world.Balance.Pterodactyl.MaxReload,
                Ammo = plane.Ammo,
                Magazine = plane.MagazineSize,
                Protection = plane.ProtectionRemaining,
                HitFlash = plane.HitFlash
            };
        }

        var pilots = new SnapshotPilot[world.Pilots.Count];

        for (var i = 0; i < world.Pilots.Count; i++)
        {
            var pilot = world.Pilots[i];
            pilots[i] = new SnapshotPilot
            {
                Slot = pilot.Slot,
                IsBot = pilot.IsBot,
                Kind = ToKindName(pilot.Kind),
                State = ToPilotStateName(pilot.State),
                X = pilot.Position.X,
                Y = pilot.Position.Y,
                Vy = pilot.Velocity.Y,
                Facing = pilot.Facing,
                ParachuteBroken = pilot.IsParachuteBroken
            };
        }

        var projectiles = new SnapshotProjectile[world.Projectiles.Count];

        for (var i = 0; i < world.Projectiles.Count; i++)
        {
            var projectile = world.Projectiles[i];
            projectiles[i] = new SnapshotProjectile
            {
                X = projectile.Position.X,
                Y = projectile.Position.Y,
                Angle = projectile.Rotation,
                Kind = projectile.Kind == ProjectileKind.Spear ? "spear" : "bullet",
                Slot = projectile.OwnerSlot,
                Radius = projectile.Radius,
                Length = projectile.Kind == ProjectileKind.Spear ? 40f : 0f
            };
        }

        IReadOnlyList<GameEvent> sourceEvents = events ?? world.Events;
        var snapshotEvents = new SnapshotEvent[sourceEvents.Count];

        for (var i = 0; i < sourceEvents.Count; i++)
        {
            var gameEvent = sourceEvents[i];
            snapshotEvents[i] = new SnapshotEvent
            {
                Type = ToEventName(gameEvent.Type),
                X = gameEvent.Position.X,
                Y = gameEvent.Position.Y,
                Slot = gameEvent.Slot,
                Value = gameEvent.Value
            };
        }

        return new GameSnapshot
        {
            Status = status,
            YourSlot = yourSlot,
            WinnerSlot = world.WinnerSlot,
            Scores = [world.Score(0), world.Score(1)],
            Elapsed = world.Elapsed,
            WorldWidth = GameBalance.WorldWidth,
            WorldHeight = GameBalance.WorldHeight,
            GroundY = GameBalance.GroundY,
            SafeLandingSpeed = GameBalance.PilotSafeLandingSpeed,
            Tower = new SnapshotTower
            {
                X = GameBalance.TowerX,
                BaseY = GameBalance.TowerBaseY,
                Width = GameBalance.TowerWidth,
                Height = GameBalance.TowerHeight
            },
            Planes = planes,
            Pilots = pilots,
            Projectiles = projectiles,
            Events = snapshotEvents
        };
    }

    /// <summary>
    /// Преобразует класс аппарата в строковое имя.
    /// </summary>
    /// <param name="kind">Класс аппарата.</param>
    /// <returns><c>biplane</c> или <c>pterodactyl</c>.</returns>
    public static string ToKindName(PlaneKind kind) =>
        kind == PlaneKind.Biplane ? "biplane" : "pterodactyl";

    /// <summary>
    /// Преобразует состояние лётчика в строковое имя.
    /// </summary>
    /// <param name="state">Состояние лётчика.</param>
    /// <returns>Строковое имя состояния.</returns>
    public static string ToPilotStateName(PilotState state) => state switch
    {
        PilotState.Falling => "falling",
        PilotState.Parachuting => "parachuting",
        PilotState.OnGround => "onground",
        PilotState.WaitingRespawn => "waiting",
        _ => "inplane"
    };

    private static string ToEventName(GameEventType type) => type switch
    {
        GameEventType.Shot => "shot",
        GameEventType.Hit => "hit",
        GameEventType.Explosion => "explosion",
        GameEventType.Boost => "boost",
        GameEventType.Dash => "dash",
        GameEventType.Respawn => "respawn",
        GameEventType.MatchFinished => "matchFinished",
        GameEventType.ReloadStarted => "reloadStarted",
        GameEventType.ReloadFinished => "reloadFinished",
        GameEventType.Eject => "eject",
        GameEventType.Reenter => "reenter",
        GameEventType.Landed => "landed",
        GameEventType.TowerReached => "towerReached",
        GameEventType.Crash => "crash",
        GameEventType.ParachuteOpened => "parachuteOpened",
        GameEventType.ParachuteClosed => "parachuteClosed",
        GameEventType.ParachuteDestroyed => "parachuteDestroyed",
        GameEventType.PilotKilled => "pilotKilled",
        _ => "shot"
    };
}
