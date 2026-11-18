using System.Numerics;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Состояние лётчика, покинувшего аппарат.
/// </summary>
public enum PilotState
{
    /// <summary>Лётчик находится в аппарате.</summary>
    InPlane,

    /// <summary>Лётчик падает без парашюта.</summary>
    Falling,

    /// <summary>Лётчик планирует на парашюте.</summary>
    Parachuting,

    /// <summary>Лётчик приземлился и бежит по земле.</summary>
    OnGround,

    /// <summary>Лётчик ожидает возрождения после уничтожения аппарата.</summary>
    WaitingRespawn
}

/// <summary>
/// Лётчик слота: хранит состояние парашютирования, бега и возрождения.
/// </summary>
public sealed class Pilot
{
    /// <summary>
    /// Создаёт лётчика для заданного слота.
    /// </summary>
    /// <param name="slot">Слот владельца: 0 или 1.</param>
    /// <param name="isBot">Управляется ли лётчик ботом.</param>
    /// <param name="kind">Класс аппарата, на котором летает лётчик.</param>
    public Pilot(int slot, bool isBot, PlaneKind kind)
    {
        Slot = slot;
        IsBot = isBot;
        Kind = kind;
        State = PilotState.InPlane;
    }

    /// <summary>Слот владельца: 0 или 1.</summary>
    public int Slot { get; }

    /// <summary>Управляется ли лётчик ботом.</summary>
    public bool IsBot { get; }

    /// <summary>Класс аппарата, который получит лётчик при возрождении.</summary>
    public PlaneKind Kind { get; internal set; }

    /// <summary>Текущее состояние лётчика.</summary>
    public PilotState State { get; internal set; }

    /// <summary>Позиция лётчика в мировых координатах.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>Скорость лётчика.</summary>
    public Vector2 Velocity { get; internal set; }

    /// <summary>Направление взгляда: 1 — вправо, -1 — влево.</summary>
    public float Facing { get; internal set; } = 1f;

    /// <summary>Оставшееся время до возрождения.</summary>
    public float RespawnRemaining { get; internal set; }

    /// <summary>Идентификатор аппарата, в котором находится лётчик.</summary>
    public int PlaneId { get; internal set; }

    /// <summary>Была ли кнопка покидания аппарата нажата на предыдущем шаге.</summary>
    internal bool WasEjectPressed { get; set; }

    /// <summary>Была ли кнопка парашюта нажата на предыдущем шаге.</summary>
    internal bool WasParachutePressed { get; set; }
}
