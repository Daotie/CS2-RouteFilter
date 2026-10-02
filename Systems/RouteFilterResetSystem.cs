using Colossal.Entities;
using Game;
using Game.Net;
using RouteFilter.Components;
using RouteFilter.Persistence;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Executes the explicit Reset request.
///
/// It clears exactly three things and nothing else:
/// <list type="number">
/// <item>RouteFilter's own persistent configuration (the restriction markers and forbidden-asset
/// buffers) so a later save contains zero restrictions.</item>
/// <item>RouteFilter's own owned runtime state, released by the backend that owns it with an exact
/// compare-before-restore.</item>
/// <item>RouteFilter's managed caches.</item>
/// </list>
///
/// It deliberately does not touch vanilla lane, path or vehicle state whose ownership it cannot
/// prove. A Reset must never be able to damage a city or another mod.
/// </summary>
public sealed partial class RouteFilterResetSystem : GameSystemBase
{
    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private EntityQuery m_RestrictedAssets;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_RestrictedNodes = GetEntityQuery(ComponentType.ReadOnly<NodeAssetRestrictionV1>());
        m_RestrictedSegments = GetEntityQuery(ComponentType.ReadOnly<SegmentAssetRestrictionV1>());
        m_RestrictedAssets = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
    }

    protected override void OnUpdate()
    {
        if (!Mod.ConsumeResetRequest()) return;

        // Release owned lane mutations first, then stop the readers of the derived snapshots.
        var road = World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>();
        var rail = World.GetOrCreateSystemManaged<RailEnforcementBackend>();
        road.ReleaseAll();
        rail.ReleaseAll();

        World.GetOrCreateSystemManaged<RestrictionSafetySystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RestrictionCandidateSystem>().ResetRuntimeState();

        var targets = new HashSet<Entity>();
        using (var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp))
            foreach (var entity in nodes) targets.Add(entity);
        using (var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp))
            foreach (var entity in segments) targets.Add(entity);
        using (var assets = m_RestrictedAssets.ToEntityArray(Allocator.Temp))
            foreach (var entity in assets) targets.Add(entity);

        foreach (var target in targets)
        {
            if (EntityManager.HasComponent<NodeAssetRestrictionV1>(target))
                EntityManager.RemoveComponent<NodeAssetRestrictionV1>(target);
            if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(target))
                EntityManager.RemoveComponent<SegmentAssetRestrictionV1>(target);
            if (EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target))
                EntityManager.RemoveComponent<RestrictedVehicleAssetV1>(target);
        }

        Mod.SelectedVehicleAssets.Clear();
        Mod.SelectedTargetMode = RestrictionTargetMode.Node;
        Mod.RestrictionsDirty = true;
        Mod.DebugVehicle = Entity.Null;

        var persistence = World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>();
        persistence.ResetRuntimeState();

        World.GetOrCreateSystemManaged<RestrictionIndexSystem>().ResetRuntimeState();
        World.GetOrCreateSystemManaged<RestrictionOverlaySystem>().ResetRuntimeState();
        var ui = World.GetOrCreateSystemManaged<RouteFilterUISystem>();
        ui.ResetRuntimeState();
        ui.NotifyResetCompleted();

        road.ResetRuntimeState();
        rail.ResetRuntimeState();
        RestrictionPathfindHook.ResetCounters();

        Mod.Log.Info(
            $"[RouteFilter.Reset] targetsCleared={targets.Count} " +
            $"roadLeasesReleased=owned-only roadAttemptsCleared=owned-only railAttemptsCleared=owned-only " +
            $"vanillaStateRewritten=0 unknownLaneStateRestored=0 " +
            $"persistenceUnlocked={!persistence.PersistenceLocked}");
    }
}
