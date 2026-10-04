using Game.Net;
using Unity.Mathematics;

namespace RouteFilter.Systems;

/// <summary>Chord lower bound, not curve length times a non-uniform Bezier parameter.</summary>
internal static class GateApproachDistance
{
    internal static float Remaining(in Curve curve, float current, float end)
    {
        if (!math.isfinite(current) || !math.isfinite(end) || current < 0 || current > 1 || end < 0 || end > 1)
            return float.NaN;
        return math.distance(Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, current),
            Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, end));
    }
}
