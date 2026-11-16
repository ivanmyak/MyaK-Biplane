namespace MyaKBiplane.Shared;

/// <summary>
/// Общие сведения об игре, используемые клиентами и сервером.
/// </summary>
public static class GameInfo
{
    /// <summary>Отображаемое название игры.</summary>
    public const string Title = "MyaK Biplane";

    /// <summary>Версия приложения.</summary>
    public const string Version = "0.1.0";

    /// <summary>Версия сетевого протокола. Должна совпадать у клиента и сервера.</summary>
    public const int ProtocolVersion = 1;
}
