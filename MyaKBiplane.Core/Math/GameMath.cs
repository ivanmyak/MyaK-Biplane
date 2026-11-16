using System.Numerics;

namespace MyaKBiplane.Core.Math;

/// <summary>
/// Вспомогательные математические операции, используемые симуляцией.
/// </summary>
public static class GameMath
{
    /// <summary>Полный оборот в радианах.</summary>
    public const float TwoPi = MathF.PI * 2f;

    /// <summary>
    /// Ограничивает значение диапазоном <paramref name="min"/>..<paramref name="max"/>.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <param name="min">Нижняя граница.</param>
    /// <param name="max">Верхняя граница.</param>
    /// <returns>Значение внутри заданного диапазона.</returns>
    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    /// <summary>
    /// Ограничивает значение диапазоном 0..1.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <returns>Значение в диапазоне 0..1.</returns>
    public static float Clamp01(float value) => Clamp(value, 0f, 1f);

    /// <summary>
    /// Линейно интерполирует два значения.
    /// </summary>
    /// <param name="from">Начальное значение.</param>
    /// <param name="to">Конечное значение.</param>
    /// <param name="t">Коэффициент интерполяции.</param>
    /// <returns>Интерполированное значение.</returns>
    public static float Lerp(float from, float to, float t) => from + ((to - from) * Clamp01(t));

    /// <summary>
    /// Приводит угол к диапазону -π..π.
    /// </summary>
    /// <param name="angle">Угол в радианах.</param>
    /// <returns>Нормализованный угол.</returns>
    public static float NormalizeAngle(float angle)
    {
        angle %= TwoPi;

        if (angle > MathF.PI)
        {
            angle -= TwoPi;
        }
        else if (angle < -MathF.PI)
        {
            angle += TwoPi;
        }

        return angle;
    }

    /// <summary>
    /// Вычисляет кратчайшую разницу между двумя углами.
    /// </summary>
    /// <param name="from">Текущий угол.</param>
    /// <param name="to">Целевой угол.</param>
    /// <returns>Разница в диапазоне -π..π.</returns>
    public static float AngleDelta(float from, float to) => NormalizeAngle(to - from);

    /// <summary>
    /// Возвращает единичный вектор для заданного угла.
    /// </summary>
    /// <param name="angle">Угол в радианах.</param>
    /// <returns>Единичный вектор направления.</returns>
    public static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    /// <summary>
    /// Нормализует вектор, возвращая нулевой вектор для слишком коротких значений.
    /// </summary>
    /// <param name="value">Исходный вектор.</param>
    /// <returns>Нормализованный вектор или <see cref="Vector2.Zero"/>.</returns>
    public static Vector2 NormalizeOrZero(Vector2 value)
    {
        var length = value.Length();
        return length > 1e-6f ? value / length : Vector2.Zero;
    }

    /// <summary>
    /// Ограничивает длину вектора.
    /// </summary>
    /// <param name="value">Исходный вектор.</param>
    /// <param name="maxLength">Максимальная длина.</param>
    /// <returns>Вектор с длиной не больше <paramref name="maxLength"/>.</returns>
    public static Vector2 ClampLength(Vector2 value, float maxLength)
    {
        var lengthSquared = value.LengthSquared();

        if (lengthSquared <= maxLength * maxLength || lengthSquared <= 1e-6f)
        {
            return value;
        }

        return Vector2.Normalize(value) * maxLength;
    }

    /// <summary>
    /// Поворачивает вектор на заданный угол.
    /// </summary>
    /// <param name="value">Исходный вектор.</param>
    /// <param name="angle">Угол поворота в радианах.</param>
    /// <returns>Повёрнутый вектор.</returns>
    public static Vector2 Rotate(Vector2 value, float angle)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        return new Vector2(
            (value.X * cos) - (value.Y * sin),
            (value.X * sin) + (value.Y * cos));
    }
}
