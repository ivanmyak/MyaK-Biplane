namespace MyaKBiplane.Shared.Contracts;

/// <summary>
/// Состояние комнаты, которое сервер сообщает каждому игроку отдельно.
/// </summary>
public sealed record RoomInfo
{
    /// <summary>Код комнаты. Пустой для матча с ботом.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Режим: <c>bot</c> или <c>online</c>.</summary>
    public string Mode { get; init; } = "bot";

    /// <summary>Статус: <c>waiting</c>, <c>playing</c>, <c>paused</c>, <c>finished</c> или <c>closed</c>.</summary>
    public string Status { get; init; } = "waiting";

    /// <summary>Слот, за который играет получатель.</summary>
    public int YourSlot { get; init; }

    /// <summary>Является ли получатель хозяином комнаты.</summary>
    public bool IsHost { get; init; }

    /// <summary>Класс аппарата хозяина комнаты.</summary>
    public string HostKind { get; init; } = "biplane";

    /// <summary>Класс аппарата гостя, если он уже присоединился.</summary>
    public string? GuestKind { get; init; }

    /// <summary>Правила комнаты, выбранные хозяином.</summary>
    public RoomSettings Settings { get; init; } = new();

    /// <summary>Подключён ли второй игрок.</summary>
    public bool OpponentConnected { get; init; }

    /// <summary>
    /// Установлено ли прямое соединение между игроками. В серверном матче всегда истина;
    /// в P2P-матче становится true только после открытия DataChannel.
    /// </summary>
    public bool PeerConnected { get; init; } = true;

    /// <summary>Кто поставил игру на паузу, или -1.</summary>
    public int PausedBySlot { get; init; } = -1;

    /// <summary>Предложил ли получатель реванш.</summary>
    public bool RematchOfferedByYou { get; init; }

    /// <summary>Предложил ли реванш соперник.</summary>
    public bool RematchOfferedByOpponent { get; init; }

    /// <summary>Дополнительное уведомление, например отказ от реванша.</summary>
    public string? Notice { get; init; }
}
