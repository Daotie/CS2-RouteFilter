using Colossal.Entities;
using Game;
using Game.Net;
using RouteFilter.Components;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Inverted indexes over the current restriction state. Enforcement checks become
/// O(1) lookups instead of per-frame component scans and nested asset-list walks:
/// - global set of every restricted vehicle prefab, to skip unrelated vehicles;
/// - per-target sets of forbidden prefabs, to replace buffer iteration;
/// - lane to owning restricted target map, to replace the Owner-chain crawl.
/// The indexes are rebuilt when restrictions change and periodically as a safety net.
/// </summary>
public sealed partial class RestrictionIndexSystem : GameSystemBase
{
    private const int kPeriodicRefreshTicks = 256;

    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private readonly HashSet<Entity> m_RestrictedPrefabs = new();
    private readonly Dictionary<Entity, HashSet<Entity>> m_TargetPrefabs = new();
    private readonly Dictionary<Entity, Entity> m_LaneTargets = new();
    // Start past the threshold so the index is built on the first update after load.
    private int m_RefreshTicks = kPeriodicRefreshTicks;

    public bool ContainsRestrictedPrefab(Entity prefab) => m_RestrictedPrefabs.Contains(prefab);

    public bool TargetRestricts(Entity target, IReadOnlyCollection<Entity> prefabs)
    {
        if (target == Entity.Null || !m_TargetPrefabs.TryGetValue(target, out var set)) return false;
        foreach (var prefab in prefabs)
            if (set.Contains(prefab)) return true;
        return false;
    }

    public bool TryGetRestrictedTarget(Entity lane, out Entity target)
        => m_LaneTargets.TryGetValue(lane, out target);

    protected override void OnCreate()
    {
        base.OnCreate();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Game.Net.Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
    }

    protected override void OnUpdate()
    {
        if (!Mod.RestrictionsDirty && ++m_RefreshTicks < kPeriodicRefreshTicks) return;
        Mod.RestrictionsDirty = false;
        m_RefreshTicks = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        m_RestrictedPrefabs.Clear();
        m_TargetPrefabs.Clear();
        m_LaneTargets.Clear();

        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        foreach (var node in nodes) AddTarget(node);

        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        foreach (var segment in segments) AddTarget(segment);
    }

    private void AddTarget(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets) ||
            assets.Length == 0) return;

        var set = new HashSet<Entity>();
        foreach (var asset in assets)
        {
            if (asset.m_Prefab == Entity.Null || !set.Add(asset.m_Prefab)) continue;
            m_RestrictedPrefabs.Add(asset.m_Prefab);
        }
        m_TargetPrefabs[target] = set;

        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<SubLane> lanes)) return;
        foreach (var subLane in lanes)
            if (subLane.m_SubLane != Entity.Null)
                m_LaneTargets[subLane.m_SubLane] = target;
    }
}
