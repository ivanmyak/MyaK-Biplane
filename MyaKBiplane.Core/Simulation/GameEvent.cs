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
    MatchFinished
}

/// <summary>
/// Событие, произошедшее за один шаг симуляции.
/// </summary>
/// <param name="Type">Тип события.</param>
/// <param name="Position">Позиция события в мировых координатах.</param>
/// <param name="Value">Дополнительное числовое значение (например, урон).</param>
/// <param name="IsPlayer">Относится ли событие к игроку.</param>
public readonly record struct GameEvent(
    GameEventType Type,
    System.Numerics.Vector2 Position,
    float Value = 0f,
    bool IsPlayer = false);
