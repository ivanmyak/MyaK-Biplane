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

    /// <summary>Отступ от границ мира, в пределах которого самолёт остаётся внутри арены.</summary>
    public const float WorldPadding = 32f;

    /// <summary>Максимальный запас здоровья любого класса.</summary>
    public const float MaxHealth = 100f;

    /// <summary>Задержка возрождения после уничтожения, в секундах.</summary>
    public const float RespawnDelay = 2.5f;

    /// <summary>Количество уничтожений, необходимое для победы в матче.</summary>
    public const int ScoreToWin = 5;

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

    /// <summary>Радиус пули для проверки попадания.</summary>
    public float BulletRadius { get; init; } = 5f;

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
