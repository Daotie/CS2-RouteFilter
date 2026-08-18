using Colossal.Entities;
using Game;
using Game.Net;
using Game.Pathfind;
using NodeGate.Components;
using Unity.Entities;
using Unity.Jobs;

namespace NodeGate.Systems;

public sealed partial class RestrictionPathSystem : GameSystemBase
{
    private EntityQuery m_RestrictedNodes;
    private PathfindQueueSystem m_PathfindQueueSystem = null!;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PathfindQueueSystem = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<Node>(),
            ComponentType.ReadOnly<NodeVehicleRestriction>(),
            ComponentType.ReadOnly<SubLane>());
        RequireForUpdate(m_RestrictedNodes);
    }

    protected override void OnUpdate()
    {
        var pathfindData = m_PathfindQueueSystem.GetDataContainer(out var dependencies);
        dependencies.Complete();

        using var entities = m_RestrictedNodes.ToEntityArray(Unity.Collections.Allocator.Temp);
        using var restrictions = m_RestrictedNodes.ToComponentDataArray<NodeVehicleRestriction>(Unity.Collections.Allocator.Temp);

        for (var i = 0; i < entities.Length; i++)
        {
            var rules = restrictions[i].m_Rules & NodeVehicleRestriction.SupportedRules;
            if (rules == default || !EntityManager.TryGetBuffer(entities[i], true, out DynamicBuffer<SubLane> lanes))
                continue;

            foreach (var subLane in lanes)
            {
                var laneEntity = subLane.m_SubLane;
                if (!EntityManager.HasComponent<CarLane>(laneEntity) && !EntityManager.HasComponent<TrackLane>(laneEntity))
                    continue;

                ApplyRules(pathfindData, laneEntity, rules, secondary: false);
                ApplyRules(pathfindData, laneEntity, rules, secondary: true);
            }
        }

        Dependency = default(JobHandle);
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
