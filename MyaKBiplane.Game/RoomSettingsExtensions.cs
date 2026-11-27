using MyaKBiplane.Core.Simulation;
using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Game;

/// <summary>
/// Преобразует сетевые настройки комнаты в правила симуляции.
/// </summary>
public static class RoomSettingsExtensions
{
    /// <summary>
    /// Создаёт правила матча из настроек комнаты.
    /// </summary>
    /// <param name="settings">Настройки комнаты.</param>
    /// <returns>Правила для <see cref="GameWorld"/>.</returns>
    public static MatchRules ToMatchRules(this RoomSettings settings) => new()
    {
        ScoreToWin = settings.ScoreToWin,
        AllowEjection = settings.AllowEjection
    };

    /// <summary>
    /// Определяет класс аппарата гостя с учётом запрета на одинаковые аппараты.
    /// </summary>
    /// <param name="settings">Настройки комнаты.</param>
    /// <param name="hostKind">Класс хозяина.</param>
    /// <param name="requestedKind">Класс, выбранный гостем.</param>
    /// <returns>Класс, с которым гость войдёт в матч.</returns>
    public static PlaneKind ResolveGuestKind(
        this RoomSettings settings,
        PlaneKind hostKind,
        PlaneKind requestedKind) =>
        !settings.AllowSameKind && requestedKind == hostKind
            ? GameWorld.Opposite(hostKind)
            : requestedKind;
}
