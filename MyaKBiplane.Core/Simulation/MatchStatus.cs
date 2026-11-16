namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Состояние матча.
/// </summary>
public enum MatchStatus
{
    /// <summary>Матч продолжается.</summary>
    Playing,

    /// <summary>Матч завершён победой одной из сторон.</summary>
    Finished
}

/// <summary>
/// Победитель матча.
/// </summary>
public enum MatchWinner
{
    /// <summary>Победитель ещё не определён.</summary>
    None,

    /// <summary>Победил игрок.</summary>
    Player,

    /// <summary>Победил бот.</summary>
    Bot,

    /// <summary>Ничья.</summary>
    Draw
}
