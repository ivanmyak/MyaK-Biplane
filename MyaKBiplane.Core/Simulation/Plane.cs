using System.Numerics;
using MyaKBiplane.Core.Balance;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Состояние одного летательного аппарата в мире.
/// </summary>
public sealed class Plane
{
    /// <summary>
    /// Создаёт летательный аппарат с заданным идентификатором и классом.
    /// </summary>
    /// <param name="id">Уникальный идентификатор внутри матча.</param>
    /// <param name="kind">Класс аппарата.</param>
    /// <param name="isPlayer">Управляется ли аппарат игроком.</param>
    public Plane(int id, PlaneKind kind, bool isPlayer)
    {
        Id = id;
        Kind = kind;
        IsPlayer = isPlayer;
        Health = GameBalance.MaxHealth;
    }

    /// <summary>Уникальный идентификатор внутри матча.</summary>
    public int Id { get; }

    /// <summary>Класс аппарата.</summary>
    public PlaneKind Kind { get; }

    /// <summary>Управляется ли аппарат игроком.</summary>
    public bool IsPlayer { get; }

    /// <summary>Текущая позиция в мировых координатах.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>Текущая скорость.</summary>
    public Vector2 Velocity { get; internal set; }

    /// <summary>Текущий угол поворота в радианах.</summary>
    public float Rotation { get; internal set; }

    /// <summary>Текущее здоровье.</summary>
    public float Health { get; internal set; }

    /// <summary>Количество уничтоженных противников.</summary>
    public int Score { get; internal set; }

    /// <summary>Оставшееся время до следующего выстрела биплана.</summary>
    public float FireCooldown { get; internal set; }

    /// <summary>Оставшееся время перезарядки копья птеродактиля.</summary>
    public float ReloadRemaining { get; internal set; }

    /// <summary>Текущий заряд копья в диапазоне 0..1.</summary>
    public float Charge { get; internal set; }

    /// <summary>Оставшееся время действия ускорения биплана.</summary>
    public float BoostTimeRemaining { get; internal set; }

    /// <summary>Оставшееся время кулдауна ускорения.</summary>
    public float BoostCooldownRemaining { get; internal set; }

    /// <summary>Оставшееся время кулдауна рывка.</summary>
    public float DashCooldownRemaining { get; internal set; }

    /// <summary>Оставшееся время до возрождения.</summary>
    public float RespawnRemaining { get; internal set; }

    /// <summary>Оставшееся время подсветки попадания.</summary>
    public float HitFlash { get; internal set; }

    /// <summary>Была ли кнопка огня нажата на предыдущем шаге.</summary>
    internal bool WasFirePressed { get; set; }

    /// <summary>Была ли кнопка способности нажата на предыдущем шаге.</summary>
    internal bool WasAbilityPressed { get; set; }

    /// <summary>Жив ли аппарат.</summary>
    public bool IsAlive => Health > 0f;

    /// <summary>Действует ли ускорение.</summary>
    public bool IsBoosting => BoostTimeRemaining > 0f;

    /// <summary>Идёт ли зарядка копья.</summary>
    public bool IsCharging => Charge > 0f;

    /// <summary>Радиус аппарата для проверки столкновений.</summary>
    public float Radius => Kind == PlaneKind.Biplane ? 24f : 22f;

    /// <summary>
    /// Сбрасывает состояние аппарата для возрождения.
    /// </summary>
    /// <param name="position">Позиция возрождения.</param>
    /// <param name="rotation">Начальный угол.</param>
    internal void ResetForSpawn(Vector2 position, float rotation)
    {
        Position = position;
        Velocity = Vector2.Zero;
        Rotation = rotation;
        Health = GameBalance.MaxHealth;
        FireCooldown = 0f;
        ReloadRemaining = 0f;
        Charge = 0f;
        BoostTimeRemaining = 0f;
        BoostCooldownRemaining = 0f;
        DashCooldownRemaining = 0f;
        RespawnRemaining = 0f;
        HitFlash = 0f;
        WasFirePressed = false;
        WasAbilityPressed = false;
    }
}
