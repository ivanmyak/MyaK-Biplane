using System.Numerics;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Тип снаряда.
/// </summary>
public enum ProjectileKind
{
    /// <summary>Пуля биплана.</summary>
    Bullet,

    /// <summary>Копьё птеродактиля.</summary>
    Spear
}

/// <summary>
/// Снаряд, летящий по прямой.
/// </summary>
public sealed class Projectile
{
    /// <summary>
    /// Создаёт снаряд.
    /// </summary>
    /// <param name="id">Уникальный идентификатор снаряда.</param>
    /// <param name="kind">Тип снаряда.</param>
    /// <param name="ownerSlot">Слот владельца.</param>
    /// <param name="position">Начальная позиция.</param>
    /// <param name="velocity">Скорость снаряда.</param>
    /// <param name="damage">Урон при попадании.</param>
    /// <param name="radius">Радиус для проверки столкновения.</param>
    /// <param name="lifetime">Время жизни в секундах.</param>
    public Projectile(
        int id,
        ProjectileKind kind,
        int ownerSlot,
        Vector2 position,
        Vector2 velocity,
        float damage,
        float radius,
        float lifetime)
    {
        Id = id;
        Kind = kind;
        OwnerSlot = ownerSlot;
        Position = position;
        Velocity = velocity;
        Damage = damage;
        Radius = radius;
        Lifetime = lifetime;
    }

    /// <summary>Уникальный идентификатор снаряда.</summary>
    public int Id { get; }

    /// <summary>Тип снаряда.</summary>
    public ProjectileKind Kind { get; }

    /// <summary>Слот владельца.</summary>
    public int OwnerSlot { get; }

    /// <summary>Текущая позиция.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>Текущая скорость.</summary>
    public Vector2 Velocity { get; internal set; }

    /// <summary>Урон при попадании.</summary>
    public float Damage { get; }

    /// <summary>Радиус для проверки столкновения.</summary>
    public float Radius { get; }

    /// <summary>Оставшееся время жизни.</summary>
    public float Lifetime { get; internal set; }

    /// <summary>Угол направления полёта в радианах.</summary>
    public float Rotation => MathF.Atan2(Velocity.Y, Velocity.X);
}
