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
    private readonly HashSet<Entity> m_SeenTargets = new();
    private readonly List<Entity> m_StaleTargets = new();
    // Start past the threshold so the index is built on the first update after load.
    private int m_RefreshTicks = kPeriodicRefreshTicks;

    /// <summary>Increments on every rebuild; consumers can re-sync their caches on change.</summary>
    public int Version { get; private set; }

    /// <summary>Every vehicle prefab restricted by at least one target.</summary>
    public IReadOnlyCollection<Entity> RestrictedPrefabs => m_RestrictedPrefabs;

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
        Version++;
        m_RestrictedPrefabs.Clear();
        m_LaneTargets.Clear();
        m_SeenTargets.Clear();

        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        foreach (var node in nodes) AddTarget(node);

        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        foreach (var segment in segments) AddTarget(segment);

        // Drop targets that disappeared since the previous rebuild (the periodic safety net
        // rebuilds even without a dirty flag, so stale entries must be pruned here).
        m_StaleTargets.Clear();
        foreach (var target in m_TargetPrefabs.Keys)
            if (!m_SeenTargets.Contains(target)) m_StaleTargets.Add(target);
        foreach (var target in m_StaleTargets) m_TargetPrefabs.Remove(target);
    }

    private void AddTarget(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets) ||
            assets.Length == 0) return;

        m_SeenTargets.Add(target);

        // Reuse the per-target set across rebuilds; steady-state rebuilds stay allocation free.
        if (!m_TargetPrefabs.TryGetValue(target, out var set))
        {
            set = new HashSet<Entity>();
            m_TargetPrefabs[target] = set;
        }
        else
        {
            set.Clear();
        }

        foreach (var asset in assets)
        {
            if (asset.m_Prefab == Entity.Null || !set.Add(asset.m_Prefab)) continue;
            m_RestrictedPrefabs.Add(asset.m_Prefab);
        }

        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<SubLane> lanes)) return;
        foreach (var subLane in lanes)
            if (subLane.m_SubLane != Entity.Null)
                m_LaneTargets[subLane.m_SubLane] = target;
    }
}
