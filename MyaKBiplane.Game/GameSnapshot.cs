namespace MyaKBiplane.Game;

/// <summary>
/// Снимок состояния матча, передаваемый рендереру.
/// </summary>
public sealed class GameSnapshot
{
    /// <summary>Статус матча: <c>playing</c> или <c>finished</c>.</summary>
    public string Status { get; init; } = "playing";

    /// <summary>Победитель: <c>none</c>, <c>player</c>, <c>bot</c> или <c>draw</c>.</summary>
    public string Winner { get; init; } = "none";

    /// <summary>Счёт игрока.</summary>
    public int PlayerScore { get; init; }

    /// <summary>Счёт бота.</summary>
    public int BotScore { get; init; }

    /// <summary>Время матча в секундах.</summary>
    public float Elapsed { get; init; }

    /// <summary>Ширина мира.</summary>
    public float WorldWidth { get; init; }

    /// <summary>Высота мира.</summary>
    public float WorldHeight { get; init; }

    /// <summary>Класс аппарата игрока.</summary>
    public string PlayerKind { get; init; } = "biplane";

    /// <summary>Класс аппарата бота.</summary>
    public string BotKind { get; init; } = "pterodactyl";

    /// <summary>Состояния аппаратов.</summary>
    public IReadOnlyList<SnapshotPlane> Planes { get; init; } = Array.Empty<SnapshotPlane>();

    /// <summary>Состояния снарядов.</summary>
    public IReadOnlyList<SnapshotProjectile> Projectiles { get; init; } = Array.Empty<SnapshotProjectile>();

    /// <summary>Частицы визуальных эффектов.</summary>
    public IReadOnlyList<SnapshotParticle> Particles { get; init; } = Array.Empty<SnapshotParticle>();
}

/// <summary>
/// Состояние одного аппарата для рендерера.
/// </summary>
public sealed class SnapshotPlane
{
    /// <summary>Идентификатор аппарата.</summary>
    public int Id { get; init; }

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

    /// <summary>Класс аппарата.</summary>
    public string Kind { get; init; } = "biplane";

    /// <summary>Управляется ли аппарат игроком.</summary>
    public bool IsPlayer { get; init; }

    /// <summary>Жив ли аппарат.</summary>
    public bool Alive { get; init; }

    /// <summary>Счёт аппарата.</summary>
    public int Score { get; init; }

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

    /// <summary>Оставшееся время подсветки попадания.</summary>
    public float HitFlash { get; init; }
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

    /// <summary>Принадлежит ли снаряд игроку.</summary>
    public bool IsPlayer { get; init; }

    /// <summary>Длина снаряда для отрисовки.</summary>
    public float Length { get; init; }
}

/// <summary>
/// Частица визуального эффекта.
/// </summary>
public sealed class SnapshotParticle
{
    /// <summary>Позиция по X.</summary>
    public float X { get; init; }

    /// <summary>Позиция по Y.</summary>
    public float Y { get; init; }

    /// <summary>Размер частицы.</summary>
    public float Size { get; init; }

    /// <summary>Цвет в формате CSS.</summary>
    public string Color { get; init; } = "#ffffff";

    /// <summary>Прозрачность в диапазоне 0..1.</summary>
    public float Alpha { get; init; }
}
