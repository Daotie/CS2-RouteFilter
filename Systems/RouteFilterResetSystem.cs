using Game;
using Game.Net;
using Colossal.Entities;
using RouteFilter.Components;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Executes the explicit Settings reset request. It removes only RouteFilter-owned ECS
/// data and clears RouteFilter-managed caches. Shared vanilla lane/path state is observed
/// for diagnostics and not rewritten. Current 2.0 leases are released separately by
/// their owner with exact compare-before-restore; no legacy ownership is inferred.
/// </summary>
public sealed partial class RouteFilterResetSystem : GameSystemBase
{
    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private EntityQuery m_DetourBlocks;
    private EntityQuery m_DetourRequests;
    private EntityQuery m_RerouteCooldowns;
    private EntityQuery m_RestrictedAssets;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_RestrictedNodes = GetEntityQuery(ComponentType.ReadOnly<NodeAssetRestrictionV1>());
        m_RestrictedSegments = GetEntityQuery(ComponentType.ReadOnly<SegmentAssetRestrictionV1>());
        m_DetourBlocks = GetEntityQuery(ComponentType.ReadOnly<AccessDetourBlock>());
        m_DetourRequests = GetEntityQuery(ComponentType.ReadOnly<VehicleDetourRequest>());
        m_RerouteCooldowns = GetEntityQuery(ComponentType.ReadOnly<RerouteCooldown>());
        m_RestrictedAssets = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
    }

    protected override void OnUpdate()
    {
        if (!Mod.ConsumeResetRequest()) return;

        World.GetOrCreateSystemManaged<RestrictionLeaseSystem>().ReleaseAll();

        // Finish readers before clearing their producer snapshots or removing ECS data.
        World.GetOrCreateSystemManaged<RestrictionSafetySystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RestrictionCandidateSystem>().ResetRuntimeState();

        var targets = new HashSet<Entity>();
        using (var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp))
            foreach (var entity in nodes) targets.Add(entity);
        using (var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp))
            foreach (var entity in segments) targets.Add(entity);
        using (var assets = m_RestrictedAssets.ToEntityArray(Allocator.Temp))
            foreach (var entity in assets) targets.Add(entity);

        var probableLegacyBlockedLanes = CountProbableLegacyBlockedLanes();
        foreach (var target in targets)
        {
            if (EntityManager.HasComponent<NodeAssetRestrictionV1>(target))
                EntityManager.RemoveComponent<NodeAssetRestrictionV1>(target);
            if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(target))
                EntityManager.RemoveComponent<SegmentAssetRestrictionV1>(target);
            if (EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target))
                EntityManager.RemoveComponent<RestrictedVehicleAssetV1>(target);
        }

        var removedBlocks = RemoveOwnedComponent<AccessDetourBlock>(m_DetourBlocks);
        var removedRequests = RemoveOwnedComponent<VehicleDetourRequest>(m_DetourRequests);
        var removedCooldowns = RemoveOwnedComponent<RerouteCooldown>(m_RerouteCooldowns);

        Mod.SelectedVehicleAssets.Clear();
        Mod.SelectedTargetMode = RestrictionTargetMode.Node;
        Mod.RestrictionsDirty = true;
        Mod.DebugVehicle = Entity.Null;

        World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RestrictionIndexSystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RestrictionOverlaySystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RouteFilterUISystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RouteFilterUISystem>().NotifyResetCompleted();

        Mod.Log.Info(
            $"[RouteFilter.Reset] targets={targets.Count}, legacyComponents=" +
            $"{removedBlocks + removedRequests + removedCooldowns} " +
            $"(blocks={removedBlocks}, requests={removedRequests}, cooldowns={removedCooldowns}), " +
            $"probableLegacyBlockedLanesObserved={probableLegacyBlockedLanes}, " +
            "unknownLegacyLaneStateRestored=0, ownedLeaseReleaseInvoked=true, pathStateRewritten=0, automaticEnforcementActive=false");
    }

    private int CountProbableLegacyBlockedLanes()
    {
        var count = 0;
        using var blockTargets = m_DetourBlocks.ToEntityArray(Allocator.Temp);
        foreach (var target in blockTargets)
        {
            if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<SubLane> subLanes)) continue;
            foreach (var subLane in subLanes)
            {
                if (!EntityManager.TryGetComponent(subLane.m_SubLane, out CarLane lane)) continue;
                if (lane.m_BlockageStart != 0 || lane.m_BlockageEnd != 0) count++;
            }
        }
        return count;
    }

    private int RemoveOwnedComponent<T>(EntityQuery query) where T : unmanaged, IComponentData
    {
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
            if (EntityManager.HasComponent<T>(entity)) EntityManager.RemoveComponent<T>(entity);
        return entities.Length;
    }
}
