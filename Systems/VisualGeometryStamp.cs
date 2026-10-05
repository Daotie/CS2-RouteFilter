using Colossal.Entities;
using Game.Common;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilter.Systems;

internal static class VisualGeometryStamp
{
    // Editor-only readiness stamp; never changes the enforcement topology/cache.
    internal static uint ReadEditor(EntityManager manager, Entity target)
    {
        uint hash = ReadRoad(manager, target);
        if (manager.TryGetComponent(target, out Edge edge))
        {
            hash = hash * 31u ^ ReadConnections(manager, edge.m_Start);
            hash = hash * 31u ^ ReadConnections(manager, edge.m_End);
        }
        else hash = hash * 31u ^ ReadConnections(manager, target);
        return hash;
    }
    private static uint ReadConnections(EntityManager manager, Entity node)
    {
        uint hash = Read(manager, node);
        if (manager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
            foreach (var edge in edges) hash = hash * 31u ^ ReadRoad(manager, edge.m_Edge);
        return hash;
    }
    private static uint ReadRoad(EntityManager manager, Entity road)
    {
        uint hash = Read(manager, road);
        if (manager.TryGetBuffer(road, true, out DynamicBuffer<SubLane> lanes))
            foreach (var lane in lanes) hash = hash * 31u ^ Read(manager, lane.m_SubLane);
        return hash;
    }
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
