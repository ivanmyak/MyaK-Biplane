using MyaKBiplane.Core.Balance;

namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Правила конкретного матча, которые хозяин комнаты может изменить.
/// </summary>
/// <remarks>
/// Набор отделён от <see cref="GameBalance"/>: баланс задаёт физику и вооружение,
/// а правила — только условия победы и доступные механики.
/// </remarks>
public sealed record MatchRules
{
    /// <summary>Минимальное количество уничтожений для победы.</summary>
    public const int MinScoreToWin = 1;

    /// <summary>Максимальное количество уничтожений для победы.</summary>
    public const int MaxScoreToWin = 50;

    /// <summary>Правила по умолчанию: стандартный лимит и все механики доступны.</summary>
    public static MatchRules Default { get; } = new();

    /// <summary>Количество уничтожений, необходимое для победы.</summary>
    public int ScoreToWin { get; init; } = GameBalance.ScoreToWin;

    /// <summary>Разрешено ли лётчику катапультироваться из аппарата.</summary>
    public bool AllowEjection { get; init; } = true;

    /// <summary>Приводит значения к допустимым границам.</summary>
    /// <returns>Нормализованные правила.</returns>
    public MatchRules Normalized() => this with
    {
        ScoreToWin = System.Math.Clamp(ScoreToWin, MinScoreToWin, MaxScoreToWin)
    };
}
