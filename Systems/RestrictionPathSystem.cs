using Colossal.Entities;
using Game;
using Game.Net;
using Game.Pathfind;
using RouteFilter.Components;
using Unity.Entities;
using Unity.Jobs;

namespace RouteFilter.Systems;

public sealed partial class RestrictionPathSystem : GameSystemBase
{
    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private PathfindQueueSystem m_PathfindQueueSystem = null!;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PathfindQueueSystem = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<Node>(),
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<SubLane>(),
            ComponentType.ReadOnly<AccessDetourBlock>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Game.Net.Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<SubLane>(),
            ComponentType.ReadOnly<AccessDetourBlock>());
    }

    protected override void OnUpdate()
    {
        var pathfindData = m_PathfindQueueSystem.GetDataContainer(out var dependencies);
        dependencies.Complete();

        ApplyRestrictions(pathfindData, m_RestrictedNodes);
        ApplyRestrictions(pathfindData, m_RestrictedSegments);
        Dependency = default(JobHandle);
    }

    private void ApplyRestrictions(NativePathfindData pathfindData, EntityQuery query)
    {
        using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

        for (var i = 0; i < entities.Length; i++)
        {
            if (!EntityManager.TryGetBuffer(entities[i], true, out DynamicBuffer<SubLane> lanes))
                continue;

            foreach (var subLane in lanes)
            {
                var laneEntity = subLane.m_SubLane;
                if (!EntityManager.HasComponent<CarLane>(laneEntity) && !EntityManager.HasComponent<TrackLane>(laneEntity))
                    continue;

                ApplyRules(pathfindData, laneEntity, RuleFlags.HasBlockage, secondary: false);
                ApplyRules(pathfindData, laneEntity, RuleFlags.HasBlockage, secondary: true);
            }
        }

    }

    private static void ApplyRules(NativePathfindData data, Entity owner, RuleFlags rules, bool secondary)
    {
        EdgeID edgeId;
        var found = secondary ? data.GetSecondaryEdge(owner, out edgeId) : data.GetEdge(owner, out edgeId);
        if (!found)
            return;

        var unsafeData = data.GetReadOnlyData();
        ref var edge = ref unsafeData.GetEdge(edgeId);
        edge.m_Specification.m_Rules |= rules;
    }
}
