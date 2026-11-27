namespace MyaKBiplane.Shared.Contracts;

/// <summary>
/// Настройки комнаты, которые выбирает хозяин при её создании.
/// </summary>
public sealed record RoomSettings
{
    /// <summary>Минимальное количество уничтожений для победы.</summary>
    public const int MinScoreToWin = 1;

    /// <summary>Максимальное количество уничтожений для победы.</summary>
    public const int MaxScoreToWin = 50;

    /// <summary>Стандартное количество уничтожений для победы.</summary>
    public const int DefaultScoreToWin = 10;

    /// <summary>Стандартные настройки: текущие правила игры.</summary>
    public static RoomSettings Default { get; } = new();

    /// <summary>Количество уничтожений, необходимое для победы.</summary>
    public int ScoreToWin { get; init; } = DefaultScoreToWin;

    /// <summary>Можно ли обоим игрокам выбрать одинаковый аппарат.</summary>
    public bool AllowSameKind { get; init; } = true;

    /// <summary>Разрешено ли катапультирование из аппарата.</summary>
    public bool AllowEjection { get; init; } = true;

    /// <summary>Отличаются ли настройки от стандартных.</summary>
    public bool IsDefault =>
        ScoreToWin == DefaultScoreToWin && AllowSameKind && AllowEjection;

    /// <summary>Приводит значения к допустимым границам.</summary>
    /// <returns>Нормализованные настройки.</returns>
    public RoomSettings Normalized() => this with
    {
        ScoreToWin = Math.Clamp(ScoreToWin, MinScoreToWin, MaxScoreToWin)
    };
}
