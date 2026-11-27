namespace MyaKBiplane.Shared.Contracts;

/// <summary>
/// Снимок состояния матча, передаваемый рендереру.
/// </summary>
/// <remarks>
/// Сервер сериализует снимок в JSON и отправляет его клиентам. Поля намеренно
/// не зависят от Core, чтобы клиент и сервер использовали один контракт.
/// </remarks>
public sealed class GameSnapshot
{
    /// <summary>Статус матча: <c>playing</c>, <c>paused</c> или <c>finished</c>.</summary>
    public string Status { get; init; } = "playing";

    /// <summary>Слот, за который играет получатель снимка.</summary>
    public int YourSlot { get; init; }

    /// <summary>Слот победителя или -1, если матч ещё идёт.</summary>
    public int WinnerSlot { get; init; } = -1;

    /// <summary>Счёт слотов: [0] — левый, [1] — правый.</summary>
    public int[] Scores { get; init; } = [0, 0];

    /// <summary>Время матча в секундах.</summary>
    public float Elapsed { get; init; }

    /// <summary>Ширина мира.</summary>
    public float WorldWidth { get; init; }

    /// <summary>Высота мира.</summary>
    public float WorldHeight { get; init; }

    /// <summary>Уровень земли.</summary>
    public float GroundY { get; init; }

    /// <summary>Безопасная скорость касания земли для лётчика.</summary>
    public float SafeLandingSpeed { get; init; }

    /// <summary>Параметры башни-маяка.</summary>
    public SnapshotTower Tower { get; init; } = new();

    /// <summary>Состояния аппаратов, включая бесхозные.</summary>
    public SnapshotPlane[] Planes { get; init; } = [];

    /// <summary>Состояния лётчиков.</summary>
    public SnapshotPilot[] Pilots { get; init; } = [];

    /// <summary>Состояния снарядов.</summary>
    public SnapshotProjectile[] Projectiles { get; init; } = [];

    /// <summary>События, произошедшие между снимками.</summary>
    public SnapshotEvent[] Events { get; init; } = [];
}

/// <summary>
/// Параметры башни-маяка для отрисовки.
/// </summary>
public sealed class SnapshotTower
{
    /// <summary>Позиция центра башни по X.</summary>
    public float X { get; init; }

    /// <summary>Позиция основания башни по Y.</summary>
    public float BaseY { get; init; }

    /// <summary>Ширина башни.</summary>
    public float Width { get; init; }

    /// <summary>Высота башни.</summary>
    public float Height { get; init; }
}

/// <summary>
/// Состояние одного аппарата для рендерера.
/// </summary>
public sealed class SnapshotPlane
{
    /// <summary>Идентификатор аппарата.</summary>
    public int Id { get; init; }

    /// <summary>Слот владельца.</summary>
    public int Slot { get; init; }

    /// <summary>Класс аппарата: <c>biplane</c> или <c>pterodactyl</c>.</summary>
    public string Kind { get; init; } = "biplane";

    /// <summary>Управляется ли аппарат ботом.</summary>
    public bool IsBot { get; init; }

    /// <summary>Оставлен ли аппарат лётчиком.</summary>
    public bool Abandoned { get; init; }

    /// <summary>Жив ли аппарат.</summary>
    public bool Alive { get; init; }

    /// <summary>Позиция по X.</summary>
    public float X { get; init; }

    /// <summary>Позиция по Y.</summary>
    public float Y { get; init; }

    /// <summary>Угол поворота в радианах.</summary>
    public float Angle { get; init; }

    /// <summary>Текущее здоровье.</summary>
    public float Health { get; init; }

    /// <summary>Максимальное здоровье.</summary>
    public float MaxHealth { get; init; }

    /// <summary>Оставшееся время ускорения.</summary>
    public float Boost { get; init; }

    /// <summary>Оставшееся время кулдауна ускорения.</summary>
    public float BoostCooldown { get; init; }

    /// <summary>Оставшееся время кулдауна рывка.</summary>
    public float DashCooldown { get; init; }

    /// <summary>Текущий заряд копья.</summary>
    public float Charge { get; init; }

    /// <summary>Оставшееся время перезарядки.</summary>
    public float Reload { get; init; }

    /// <summary>Полная длительность перезарядки.</summary>
    public float ReloadDuration { get; init; }

    /// <summary>Количество патронов в магазине биплана.</summary>
    public int Ammo { get; init; }

    /// <summary>Размер магазина биплана.</summary>
    public int Magazine { get; init; }

    /// <summary>Оставшееся время неуязвимости после спавна.</summary>
    public float Protection { get; init; }

    /// <summary>Оставшееся время подсветки попадания.</summary>
    public float HitFlash { get; init; }
}

/// <summary>
/// Состояние лётчика для рендерера.
/// </summary>
public sealed class SnapshotPilot
{
    /// <summary>Слот владельца.</summary>
    public int Slot { get; init; }

    /// <summary>Управляется ли лётчик ботом.</summary>
    public bool IsBot { get; init; }

    /// <summary>Класс аппарата лётчика.</summary>
    public string Kind { get; init; } = "biplane";

    /// <summary>
    /// Состояние: <c>inplane</c>, <c>falling</c>, <c>parachuting</c>, <c>onground</c> или <c>waiting</c>.
    /// </summary>
    public string State { get; init; } = "inplane";

    /// <summary>Позиция по X.</summary>
    public float X { get; init; }

    /// <summary>Позиция по Y.</summary>
    public float Y { get; init; }

    /// <summary>Вертикальная скорость: положительная — падение вниз.</summary>
    public float Vy { get; init; }

    /// <summary>Направление взгляда: 1 — вправо, -1 — влево.</summary>
    public float Facing { get; init; } = 1f;

    /// <summary>
    /// Уничтожен ли парашют. Пока флаг поднят, раскрыть купол нельзя до возрождения.
    /// </summary>
    public bool ParachuteBroken { get; init; }
}

/// <summary>
/// Состояние снаряда для рендерера.
/// </summary>
public sealed class SnapshotProjectile
{
    /// <summary>Позиция по X.</summary>
    public float X { get; init; }

    /// <summary>Позиция по Y.</summary>
    public float Y { get; init; }

    /// <summary>Угол полёта в радианах.</summary>
    public float Angle { get; init; }

    /// <summary>Тип снаряда: <c>bullet</c> или <c>spear</c>.</summary>
    public string Kind { get; init; } = "bullet";

    /// <summary>Слот владельца.</summary>
    public int Slot { get; init; }

    /// <summary>Радиус пули для отрисовки.</summary>
    public float Radius { get; init; }

    /// <summary>Длина копья для отрисовки.</summary>
    public float Length { get; init; }
}

/// <summary>
/// Событие для клиентских эффектов.
/// </summary>
public sealed class SnapshotEvent
{
    /// <summary>Тип события.</summary>
    public string Type { get; init; } = "shot";

    /// <summary>Позиция по X.</summary>
    public float X { get; init; }

    /// <summary>Позиция по Y.</summary>
    public float Y { get; init; }

    /// <summary>Слот, к которому относится событие, или -1.</summary>
    public int Slot { get; init; } = -1;

    /// <summary>Дополнительное значение, например урон.</summary>
    public float Value { get; init; }
}
