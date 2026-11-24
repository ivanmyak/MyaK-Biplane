using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Client.Services;

/// <summary>
/// Матч, который ведёт клиент: сетевой через SignalR или локальный с ботом.
/// </summary>
/// <remarks>
/// Общий интерфейс нужен, чтобы страница боя не знала, где считается симуляция.
/// Позже сюда же ляжет P2P-матч, где хостом выступает браузер игрока.
/// </remarks>
public interface IMatchClient : IAsyncDisposable
{
    /// <summary>Событие обновления состояния комнаты.</summary>
    event Action<RoomInfo>? RoomUpdated;

    /// <summary>Событие получения JSON-снимка состояния.</summary>
    event Action<string>? SnapshotReceived;

    /// <summary>Событие закрытия комнаты соперником.</summary>
    event Action<string>? RoomClosed;

    /// <summary>Событие разрыва соединения.</summary>
    event Action<string>? ConnectionClosed;

    /// <summary>
    /// Адрес хаба для диагностики задержки. <c>null</c>, если матч локальный.
    /// </summary>
    Uri? HubUri { get; }

    /// <summary>Устанавливает соединение с сервером. Для локального матча ничего не делает.</summary>
    Task ConnectAsync();

    /// <summary>Создаёт матч с ботом.</summary>
    /// <param name="kind">Класс аппарата игрока.</param>
    /// <returns>Информация о комнате.</returns>
    Task<RoomInfo> CreateBotRoomAsync(string kind);

    /// <summary>Создаёт сетевую комнату.</summary>
    /// <param name="kind">Класс аппарата хозяина.</param>
    /// <returns>Информация о комнате.</returns>
    Task<RoomInfo> CreateRoomAsync(string kind);

    /// <summary>Присоединяется к комнате по коду.</summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="kind">Класс аппарата гостя.</param>
    /// <returns>Информация о комнате.</returns>
    Task<RoomInfo> JoinRoomAsync(string code, string kind);

    /// <summary>
    /// Продвигает матч на один кадр и возвращает последний JSON-снимок.
    /// </summary>
    /// <param name="input">Текущий ввод игрока.</param>
    /// <param name="timestamp">Метка времени кадра в миллисекундах.</param>
    /// <returns>JSON-снимок для рендерера.</returns>
    string Tick(InputState input, double timestamp);

    /// <summary>Переключает паузу.</summary>
    Task TogglePauseAsync();

    /// <summary>Предлагает реванш.</summary>
    Task OfferRematchAsync();

    /// <summary>Отвечает на предложение реванша.</summary>
    /// <param name="accept">Принять ли реванш.</param>
    Task RespondRematchAsync(bool accept);

    /// <summary>Выходит из комнаты.</summary>
    Task LeaveRoomAsync();

    /// <summary>
    /// Измеряет задержку до сервера. Для локального матча возвращает -1.
    /// </summary>
    /// <returns>Задержка в миллисекундах или -1.</returns>
    Task<double> MeasureRttAsync();
}
