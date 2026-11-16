namespace MyaKBiplane.Core.Simulation;

/// <summary>
/// Состояние ввода игрока за один кадр.
/// </summary>
/// <remarks>
/// Для биплана используются <see cref="Thrust"/> и <see cref="Turn"/>.
/// Для птеродактиля — <see cref="MoveX"/>, <see cref="MoveY"/> и <see cref="AimX"/>/<see cref="AimY"/>.
/// </remarks>
public struct PlayerInput
{
    /// <summary>Тяга вперёд/назад для биплана в диапазоне -1..1.</summary>
    public float Thrust { get; set; }

    /// <summary>Поворот биплана в диапазоне -1..1.</summary>
    public float Turn { get; set; }

    /// <summary>Горизонтальное перемещение птеродактиля в диапазоне -1..1.</summary>
    public float MoveX { get; set; }

    /// <summary>Вертикальное перемещение птеродактиля в диапазоне -1..1.</summary>
    public float MoveY { get; set; }

    /// <summary>Координата прицела по X в мировых единицах.</summary>
    public float AimX { get; set; }

    /// <summary>Координата прицела по Y в мировых единицах.</summary>
    public float AimY { get; set; }

    /// <summary>Удерживается ли кнопка огня.</summary>
    public bool Fire { get; set; }

    /// <summary>Нажата ли кнопка специальной способности.</summary>
    public bool Ability { get; set; }

    /// <summary>Запрошен ли перезапуск матча.</summary>
    public bool Restart { get; set; }
}
