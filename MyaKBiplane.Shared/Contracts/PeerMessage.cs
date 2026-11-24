namespace MyaKBiplane.Shared.Contracts;

/// <summary>
/// Сообщение, которым игроки обмениваются через прямой DataChannel.
/// </summary>
/// <remarks>
/// Сервер в P2P-матче не участвует: он только помогает установить соединение
/// и ретранслирует SDP/ICE до того, как канал открыт.
/// </remarks>
public sealed class PeerMessage
{
    /// <summary>
    /// Тип сообщения: <c>input</c>, <c>snapshot</c>, <c>room</c>, <c>control</c>, <c>ping</c> или <c>pong</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Ввод игрока для сообщения <c>input</c>.</summary>
    public InputState? Input { get; set; }

    /// <summary>JSON-снимок для сообщения <c>snapshot</c>.</summary>
    public string? Snapshot { get; set; }

    /// <summary>Состояние матча для сообщения <c>room</c>.</summary>
    public RoomInfo? Room { get; set; }

    /// <summary>Действие для сообщений <c>control</c>: pause, offer-rematch, respond-rematch.</summary>
    public string? Action { get; set; }

    /// <summary>Слот инициатора действия.</summary>
    public int Slot { get; set; }

    /// <summary>Ответ на предложение реванша.</summary>
    public bool Accept { get; set; }

    /// <summary>Метка времени для измерения задержки.</summary>
    public double Timestamp { get; set; }
}
