using System.Numerics;
using MyaKBiplane.Core.Balance;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Состояние одного летательного аппарата в мире.
/// </summary>
/// <remarks>
/// В мировом списке одновременно могут находиться активный аппарат игрока
/// и один или несколько бесхозных аппаратов, которые продолжают лететь после
/// катапультирования лётчика.
/// </remarks>
public sealed class Plane
{
    /// <summary>
    /// Создаёт летательный аппарат с заданным идентификатором и классом.
    /// </summary>
    /// <param name="id">Уникальный идентификатор внутри матча.</param>
    /// <param name="slot">Слот владельца: 0 или 1.</param>
    /// <param name="kind">Класс аппарата.</param>
    /// <param name="isBot">Управляется ли аппарат ботом.</param>
    public Plane(int id, int slot, PlaneKind kind, bool isBot)
    {
        Id = id;
        Slot = slot;
        Kind = kind;
        IsBot = isBot;
        Health = GameBalance.MaxHealth;
    }

    /// <summary>Уникальный идентификатор внутри матча.</summary>
    public int Id { get; }

    /// <summary>Слот владельца: 0 — левая сторона, 1 — правая сторона.</summary>
    public int Slot { get; }

    /// <summary>Класс аппарата.</summary>
    public PlaneKind Kind { get; }

    /// <summary>Управляется ли аппарат ботом.</summary>
    public bool IsBot { get; }

    /// <summary>Оставлен ли аппарат лётчиком и летит ли сам по себе.</summary>
    public bool IsAbandoned { get; internal set; }

    /// <summary>Уничтожен ли аппарат в текущем шаге.</summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>Текущая позиция в мировых координатах.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>Текущая скорость.</summary>
    public Vector2 Velocity { get; internal set; }

    /// <summary>Текущий угол поворота в радианах.</summary>
    public float Rotation { get; internal set; }

    /// <summary>Текущее здоровье.</summary>
    public float Health { get; internal set; }

    /// <summary>Оставшееся время анимации выезда на спавн.</summary>
    public float EntryRemaining { get; internal set; }

    /// <summary>Оставшееся время неуязвимости после спавна.</summary>
    public float ProtectionRemaining { get; internal set; }

    /// <summary>Оставшееся время до следующего выстрела биплана.</summary>
    public float FireCooldown { get; internal set; }

    /// <summary>Оставшееся время перезарядки магазина или копья.</summary>
    public float ReloadRemaining { get; internal set; }

    /// <summary>Количество патронов в магазине биплана.</summary>
    public int Ammo { get; internal set; }

    /// <summary>Размер магазина биплана.</summary>
    public int MagazineSize { get; internal set; }

    /// <summary>Текущий заряд копья в диапазоне 0..1.</summary>
    public float Charge { get; internal set; }

    /// <summary>Оставшееся время действия ускорения биплана.</summary>
    public float BoostTimeRemaining { get; internal set; }

    /// <summary>Оставшееся время кулдауна ускорения.</summary>
    public float BoostCooldownRemaining { get; internal set; }

    /// <summary>Оставшееся время кулдауна рывка.</summary>
    public float DashCooldownRemaining { get; internal set; }

    /// <summary>Оставшееся время подсветки попадания.</summary>
    public float HitFlash { get; internal set; }

    /// <summary>Была ли кнопка огня нажата на предыдущем шаге.</summary>
    internal bool WasFirePressed { get; set; }

    /// <summary>Была ли кнопка способности нажата на предыдущем шаге.</summary>
    internal bool WasAbilityPressed { get; set; }

    /// <summary>Жив ли аппарат.</summary>
    public bool IsAlive => !IsDestroyed && Health > 0f;

    /// <summary>Действует ли неуязвимость после спавна.</summary>
    public bool IsProtected => ProtectionRemaining > 0f;

    /// <summary>Идёт ли неуправляемая анимация появления.</summary>
    public bool IsEntering => EntryRemaining > 0f;

    /// <summary>Идёт ли перезарядка.</summary>
    public bool IsReloading => ReloadRemaining > 0f;

    /// <summary>Действует ли ускорение.</summary>
    public bool IsBoosting => BoostTimeRemaining > 0f;

    /// <summary>Идёт ли зарядка копья.</summary>
    public bool IsCharging => Charge > 0f;

    /// <summary>Радиус аппарата для проверки столкновений.</summary>
    public float Radius => Kind == PlaneKind.Biplane ? 24f : 22f;

    /// <summary>
    /// Сбрасывает состояние аппарата для возрождения.
    /// </summary>
    /// <param name="position">Стартовая позиция анимации появления.</param>
    /// <param name="rotation">Начальный угол.</param>
    /// <param name="magazineSize">Размер магазина биплана.</param>
    internal void ResetForSpawn(Vector2 position, float rotation, int magazineSize)
    {
        Position = position;
        Velocity = Vector2.Zero;
        Rotation = rotation;
        Health = GameBalance.MaxHealth;
        EntryRemaining = GameBalance.SpawnEntryDuration;
        ProtectionRemaining = GameBalance.SpawnEntryDuration + GameBalance.SpawnProtectionDuration;
        FireCooldown = 0f;
        ReloadRemaining = 0f;
        Ammo = magazineSize;
        MagazineSize = magazineSize;
        Charge = 0f;
        BoostTimeRemaining = 0f;
        BoostCooldownRemaining = 0f;
        DashCooldownRemaining = 0f;
        HitFlash = 0f;
        IsAbandoned = false;
        IsDestroyed = false;
        WasFirePressed = false;
        WasAbilityPressed = false;
    }
}
