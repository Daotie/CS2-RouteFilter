using System;

namespace RouteFilter.Systems;

internal static class SignAppearance
{
    internal static float Clamp(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
    internal static float FirstPlateHeight(float mainBottom, int count, float spacing)
    {
        // Plate height stays fixed; lift the entire assembly when needed for a tall stack.
        return Math.Max(mainBottom - .18f - .125f, .35f + .125f + Math.Max(0, count - 1) * (.25f + spacing));
    }
    internal static float PlateHeight(float first, int index, float spacing) => first - index * (.25f + spacing);
}
