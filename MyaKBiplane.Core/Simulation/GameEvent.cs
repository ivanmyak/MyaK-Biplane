namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Тип игрового события, используемого клиентом для визуальных и звуковых эффектов.
/// </summary>
public enum GameEventType
{
    /// <summary>Был произведён выстрел или бросок.</summary>
    Shot,

    /// <summary>Снаряд попал в цель.</summary>
    Hit,

    /// <summary>Летательный аппарат уничтожен.</summary>
    Explosion,

    /// <summary>Активировано ускорение биплана.</summary>
    Boost,

    /// <summary>Выполнен рывок птеродактиля.</summary>
    Dash,

    /// <summary>Летательный аппарат возродился.</summary>
    Respawn,

    /// <summary>Матч завершён.</summary>
    MatchFinished,

    /// <summary>Начата перезарядка магазина.</summary>
    ReloadStarted,

    /// <summary>Перезарядка магазина завершена.</summary>
    ReloadFinished,

    /// <summary>Лётчик покинул аппарат.</summary>
    Eject,

    /// <summary>Лётчик забрался обратно в аппарат.</summary>
    Reenter,

    /// <summary>Лётчик приземлился.</summary>
    Landed,

    /// <summary>Лётчик добрался до башни и получил новый аппарат.</summary>
    TowerReached,

    /// <summary>Аппарат разбился о землю.</summary>
    Crash,

    /// <summary>Лётчик раскрыл парашют.</summary>
    ParachuteOpened,

    /// <summary>Лётчик сложил парашют.</summary>
    ParachuteClosed,

    /// <summary>Лётчик погиб: разбился при падении или был сбит.</summary>
    PilotKilled
}

/// <summary>
/// Событие, произошедшее за один шаг симуляции.
/// </summary>
/// <param name="Type">Тип события.</param>
/// <param name="Position">Позиция события в мировых координатах.</param>
/// <param name="Value">Дополнительное числовое значение (например, урон).</param>
/// <param name="Slot">Слот, к которому относится событие, или -1.</param>
public readonly record struct GameEvent(
    GameEventType Type,
    System.Numerics.Vector2 Position,
    float Value = 0f,
    int Slot = -1);
