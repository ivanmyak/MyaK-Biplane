using MyaKBiplane.Shared.Contracts;

namespace MyaKBiplane.Client.Services;

/// <summary>
/// Матч, который ведёт клиент: локальный с ботом или P2P без сервера.
/// </summary>
/// <remarks>
/// Общий интерфейс нужен, чтобы страница боя не знала, где считается симуляция.
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
    /// Адрес хаба для диагностики задержки. Всегда <c>null</c>: сервер сигналинга не используется.
    /// </summary>
    Uri? HubUri { get; }

    /// <summary>Устанавливает соединение. Для локального и P2P-матча ничего не делает.</summary>
    Task ConnectAsync();

    /// <summary>Создаёт матч с ботом.</summary>
    /// <param name="kind">Класс аппарата игрока.</param>
    /// <returns>Информация о комнате.</returns>
    Task<RoomInfo> CreateBotRoomAsync(string kind);

    /// <summary>Создаёт сетевую комнату.</summary>
    /// <param name="kind">Класс аппарата хозяина.</param>
    /// <param name="settings">Правила комнаты.</param>
    /// <returns>Информация о комнате.</returns>
    Task<RoomInfo> CreateRoomAsync(string kind, RoomSettings settings);

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
    /// Измеряет задержку до соперника через DataChannel. Для локального матча возвращает -1.
    /// </summary>
    Task<double> MeasureRttAsync();
}

/// <summary>
/// Матч, в котором SDP-предложение и ответ передаются игроками вручную.
/// </summary>
/// <remarks>
/// Сервер сигналинга не нужен: достаточно GitHub Pages. Коды содержат SDP,
/// ICE-кандидаты, версию клиента и правила комнаты.
/// </remarks>
public interface IManualMatchClient : IMatchClient
{
    /// <summary>Код, который нужно передать сопернику (offer у хоста, answer у гостя).</summary>
    string? OutgoingCode { get; }

    /// <summary>Принимает код соперника. Хост вводит answer, гость получает offer через <see cref="IMatchClient.JoinRoomAsync"/>.</summary>
    /// <param name="code">Код соперника.</param>
    Task AcceptCodeAsync(string code);
}
