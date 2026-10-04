using Unity.Mathematics;

namespace RouteFilter.Systems;

internal static class RoadSignPlacement
{
    internal static bool TryCreate(float3 center, float3 travel, float low, float high, float groundOffset,
        out float3 left, out float3 right, out quaternion rotation)
    {
        left = right = default; rotation = quaternion.identity;
        if (!math.all(math.isfinite(center)) || !math.all(math.isfinite(travel)) ||
            !math.isfinite(low) || !math.isfinite(high) || !math.isfinite(groundOffset) ||
            high <= low || math.lengthsq(travel.xz) < .01f) return false;
        travel = math.normalize(new float3(travel.x, 0f, travel.z));
        var lateral = new float3(-travel.z, 0f, travel.x);
        left = center + lateral * (high + .8f);
        right = center + lateral * (low - .8f);
        left.y += groundOffset; right.y += groundOffset;
        rotation = quaternion.LookRotationSafe(travel, math.up());
        return true;
    }
}
