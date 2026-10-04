using Colossal.Entities;
using Game.Common;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilter.Systems;

internal static class VisualGeometryStamp
{
    internal static uint Read(EntityManager manager, Entity entity)
    {
        if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity)) return uint.MaxValue;
        uint hash = 0;
        if (manager.TryGetComponent(entity,out Curve curve))
        {
            hash = math.hash(curve.m_Bezier.a) ^ math.hash(curve.m_Bezier.b)*31u ^ math.hash(curve.m_Bezier.c)*131u ^ math.hash(curve.m_Bezier.d)*8191u;
        }
        if (manager.TryGetComponent(entity,out Node node)) hash ^= math.hash(node.m_Position);
        if (manager.TryGetBuffer(entity,true,out DynamicBuffer<SubLane> lanes))
            foreach (var lane in lanes) hash = hash*31u + (uint)lane.m_SubLane.Index ^ (uint)lane.m_SubLane.Version;
        return hash;
    }
}
