namespace MyaKBiplane.Core.Balance;

/// <summary>
/// Набор параметров баланса, используемый симуляцией.
/// </summary>
/// <remarks>
/// Значения подобраны для первого играбельного прототипа и должны уточняться
/// headless-симуляцией и плейтестами. Все размеры заданы в условных единицах мира.
/// </remarks>
public sealed class GameBalance
{
    /// <summary>Ширина игрового мира.</summary>
    public const float WorldWidth = 1600f;

    /// <summary>Высота игрового мира.</summary>
    public const float WorldHeight = 900f;

    /// <summary>Отступ от боковых и верхней границ мира.</summary>
    public const float WorldPadding = 32f;

    /// <summary>Уровень земли: при касании незащищённый аппарат разбивается.</summary>
    public const float GroundY = WorldHeight - 24f;

    /// <summary>Ширина башни-маяка.</summary>
    public const float TowerWidth = 48f;

    /// <summary>Высота башни-маяка.</summary>
    public const float TowerHeight = 250f;

    /// <summary>Позиция башни по X — строго по центру карты.</summary>
    public const float TowerX = WorldWidth * 0.5f;

    /// <summary>Основание башни стоит на земле.</summary>
    public const float TowerBaseY = GroundY;

    /// <summary>Расстояние от башни до точки спавна аппарата по горизонтали.</summary>
    public const float SpawnOffsetX = 270f;

    /// <summary>Высота точки спавна аппарата.</summary>
    public const float SpawnY = WorldHeight * 0.42f;

    /// <summary>Отступ от края мира, из которого аппарат выезжает на спавн.</summary>
    public const float SpawnEntryInset = 56f;

    /// <summary>Длительность плавного выезда и взлёта на спавне, в секундах.</summary>
    public const float SpawnEntryDuration = 2.4f;

    /// <summary>
    /// Доля горизонтального пути до контрольной точки разгона по земле.
    /// </summary>
    public const float SpawnEntryRollShare = 0.35f;

    /// <summary>
    /// Доля горизонтального пути, отведённая на подход к точке спавна под курсовым углом.
    /// </summary>
    public const float SpawnEntryApproachShare = 0.25f;

    /// <summary>Скорость аппарата в момент передачи управления после выезда.</summary>
    public const float SpawnEntrySpeed = 250f;

    /// <summary>Угол набора высоты после спавна: нос смотрит на верхний угол соперника.</summary>
    public const float SpawnApproachAngle = 0.22f;

    /// <summary>Длительность неуязвимости и запрета атаки после спавна, в секундах.</summary>
    public const float SpawnProtectionDuration = 3f;

    /// <summary>Максимальный запас здоровья любого класса.</summary>
    public const float MaxHealth = 100f;

    /// <summary>Задержка возрождения после уничтожения, в секундах.</summary>
    public const float RespawnDelay = 2.5f;

    /// <summary>Количество уничтожений, необходимое для победы в матче.</summary>
    public const int ScoreToWin = 10;

    /// <summary>Радиус лётчика: 1.7 радиуса пули биплана.</summary>
    public const float PilotRadius = 7.65f;

    /// <summary>Радиус, в пределах которого лётчик считается добравшимся до башни.</summary>
    public const float PilotReachRadius = 72f;

    /// <summary>Радиус, в пределах которого лётчик может забраться обратно в аппарат.</summary>
    public const float PilotReenterRadius = 46f;

    /// <summary>Гравитация лётчика в свободном падении.</summary>
    public const float PilotGravity = 900f;

    /// <summary>Сопротивление воздуха при свободном падении.</summary>
    public const float PilotFreefallDrag = 0.15f;

    /// <summary>Предельная скорость свободного падения.</summary>
    public const float PilotTerminalSpeed = 640f;

    /// <summary>Боковое ускорение лётчика в свободном падении.</summary>
    public const float PilotAirControlAcceleration = 220f;

    /// <summary>Максимальная боковая скорость в свободном падении.</summary>
    public const float PilotAirMaxSpeed = 170f;

    /// <summary>Торможение при раскрытии парашюта.</summary>
    public const float PilotParachuteBrake = 1500f;

    /// <summary>Горизонтальное ускорение под куполом парашюта.</summary>
    public const float PilotParachuteAcceleration = 260f;

    /// <summary>Максимальная горизонтальная скорость под куполом.</summary>
    public const float PilotParachuteMaxSpeed = 120f;

    /// <summary>Максимальная скорость снижения под куполом.</summary>
    public const float PilotParachuteFallSpeed = 105f;

    /// <summary>Скорость бега лётчика по земле.</summary>
    public const float PilotGroundSpeed = 110f;

    /// <summary>Безопасная скорость касания земли: выше неё лётчик разбивается.</summary>
    public const float PilotSafeLandingSpeed = 240f;

    /// <summary>Скорость снижения при планировании без парашюта.</summary>
    public const float PilotGlideFallSpeed = 205f;

    /// <summary>Начальный импульс катапультирования вверх.</summary>
    public const float PilotEjectImpulse = 240f;

    /// <summary>Гравитация бесхозного аппарата, чтобы он мог разбиться.</summary>
    public const float AbandonedGravity = 260f;

    /// <summary>Сопротивление воздуха для бесхозного аппарата.</summary>
    public const float AbandonedDrag = 0.35f;

    /// <summary>Максимальная скорость бесхозного аппарата.</summary>
    public const float AbandonedMaxSpeed = 720f;

    /// <summary>Параметры биплана.</summary>
    public BiplaneBalance Biplane { get; init; } = new();

    /// <summary>Параметры птеродактиля.</summary>
    public PterodactylBalance Pterodactyl { get; init; } = new();

    /// <summary>Баланс по умолчанию для прототипа.</summary>
    public static GameBalance Default { get; } = new();
}

/// <summary>
/// Параметры движения и вооружения биплана.
/// </summary>
public sealed class BiplaneBalance
{
    /// <summary>Ускорение при тяге вперёд.</summary>
    public float Thrust { get; init; } = 560f;

    /// <summary>Ускорение при обратной тяге.</summary>
    public float ReverseThrust { get; init; } = 220f;

    /// <summary>Максимальная скорость без учёта ускорения.</summary>
    public float MaxSpeed { get; init; } = 540f;

    /// <summary>Скорость поворота в радианах в секунду.</summary>
    public float TurnRate { get; init; } = 3f;

    /// <summary>Ускорение свободного падения.</summary>
    public float Gravity { get; init; } = 170f;

    /// <summary>Коэффициент сопротивления воздуха в секунду.</summary>
    public float Drag { get; init; } = 0.25f;

    /// <summary>Интервал между выстрелами в секундах.</summary>
    public float FireInterval { get; init; } = 1f / 1.5f;

    /// <summary>Урон одной пули.</summary>
    public float BulletDamage { get; init; } = 25f;

    /// <summary>Скорость пули.</summary>
    public float BulletSpeed { get; init; } = 950f;

    /// <summary>Время жизни пули в секундах.</summary>
    public float BulletLifetime { get; init; } = 1.5f;

    /// <summary>Радиус пули-«горошины» для проверки попадания и отрисовки.</summary>
    public float BulletRadius { get; init; } = 4.5f;

    /// <summary>Количество патронов в магазине биплана.</summary>
    public int MagazineSize { get; init; } = 5;

    /// <summary>
    /// Длительность ручной перезарядки биплана.
    /// В 2.5 раза длиннее полной зарядки копья птеродактиля (2 секунды).
    /// </summary>
    public float ReloadDuration { get; init; } = 2.5f * 2f;

    /// <summary>Множитель скорости во время ускорения.</summary>
    public float BoostMultiplier { get; init; } = 1.5f;

    /// <summary>Длительность ускорения в секундах.</summary>
    public float BoostDuration { get; init; } = 5f;

    /// <summary>Кулдаун ускорения после окончания действия, в секундах.</summary>
    public float BoostCooldown { get; init; } = 12f;
}

/// <summary>
/// Параметры движения и вооружения птеродактиля.
/// </summary>
public sealed class PterodactylBalance
{
    /// <summary>Ускорение при управлении направлением движения.</summary>
    public float Acceleration { get; init; } = 950f;

    /// <summary>Максимальная скорость.</summary>
    public float MaxSpeed { get; init; } = 400f;

    /// <summary>Коэффициент сопротивления воздуха в секунду.</summary>
    public float Drag { get; init; } = 4f;

    /// <summary>Дополнительный импульс рывка.</summary>
    public float DashSpeed { get; init; } = 700f;

    /// <summary>Кулдаун рывка в секундах.</summary>
    public float DashCooldown { get; init; } = 2.5f;

    /// <summary>Время полной зарядки копья в секундах.</summary>
    public float ChargeTime { get; init; } = 1.2f;

    /// <summary>Минимальный урон копья при мгновенном броске.</summary>
    public float MinDamage { get; init; } = 12.5f;

    /// <summary>Максимальный урон копья при полной зарядке.</summary>
    public float MaxDamage { get; init; } = 50f;

    /// <summary>Минимальная перезарядка после мгновенного броска.</summary>
    public float MinReload { get; init; } = 0.5f;

    /// <summary>Максимальная перезарядка после полной зарядки.</summary>
    public float MaxReload { get; init; } = 2f;

    /// <summary>Минимальная скорость копья.</summary>
    public float MinSpearSpeed { get; init; } = 550f;

    /// <summary>Максимальная скорость копья.</summary>
    public float MaxSpearSpeed { get; init; } = 1200f;

    /// <summary>Время жизни копья в секундах.</summary>
    public float SpearLifetime { get; init; } = 2.2f;

    /// <summary>Радиус копья для проверки попадания.</summary>
    public float SpearRadius { get; init; } = 6f;
}
