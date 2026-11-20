namespace MyaKBiplane.Shared.Contracts;

/// <summary>
/// Ввод игрока, отправляемый клиентом на сервер.
/// </summary>
public sealed class InputState
{
    /// <summary>Тяга вперёд/назад для биплана в диапазоне -1..1.</summary>
    public float Thrust { get; set; }

    /// <summary>Поворот биплана в диапазоне -1..1.</summary>
    public float Turn { get; set; }

    /// <summary>Горизонтальное перемещение птеродактиля или парашютиста.</summary>
    public float MoveX { get; set; }

    /// <summary>Вертикальное перемещение птеродактиля.</summary>
    public float MoveY { get; set; }

    /// <summary>Координата прицела по X.</summary>
    public float AimX { get; set; }

    /// <summary>Координата прицела по Y.</summary>
    public float AimY { get; set; }

    /// <summary>Удерживается ли кнопка огня.</summary>
    public bool Fire { get; set; }

    /// <summary>Нажата ли кнопка специальной способности.</summary>
    public bool Ability { get; set; }

    /// <summary>Запрошена ли ручная перезарядка биплана.</summary>
    public bool Reload { get; set; }

    /// <summary>Покинуть аппарат или забраться обратно.</summary>
    public bool Eject { get; set; }

    /// <summary>Раскрыть или сложить парашют при свободном падении.</summary>
    public bool Parachute { get; set; }
}
