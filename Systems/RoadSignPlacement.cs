using Unity.Mathematics;

namespace RouteFilter.Systems;

internal static class RoadSignPlacement
{
    // Narrow gaps between opposing carriageways are painted dividers, not space
    // for an 0.8m verge offset. Wide medians retain the existing verge placement.
    internal static void ConstrainToDivider(float low, float high, float otherLow, float otherHigh,
        ref float leftMargin, ref float rightMargin)
    {
        var leftGap = otherLow - high;
        var rightGap = low - otherHigh;
        if (leftGap >= -.35f && leftGap < 1.6f) leftMargin = leftGap * .5f;
        if (rightGap >= -.35f && rightGap < 1.6f) rightMargin = rightGap * .5f;
    }

    internal static bool TryCreate(float3 center, float3 travel, float low, float high, float groundOffset,
        out float3 left, out float3 right, out quaternion rotation, float leftMargin = .8f, float rightMargin = .8f)
    {
        left = right = default; rotation = quaternion.identity;
        if (!math.all(math.isfinite(center)) || !math.all(math.isfinite(travel)) ||
            !math.isfinite(low) || !math.isfinite(high) || !math.isfinite(groundOffset) ||
            high <= low || math.lengthsq(travel.xz) < .01f) return false;
        travel = math.normalize(new float3(travel.x, 0f, travel.z));
        var lateral = new float3(-travel.z, 0f, travel.x);
        left = center + lateral * (high + leftMargin);
        right = center + lateral * (low - rightMargin);
        left.y += groundOffset; right.y += groundOffset;
        rotation = quaternion.LookRotationSafe(-travel, math.up());
        return true;
    }
}
