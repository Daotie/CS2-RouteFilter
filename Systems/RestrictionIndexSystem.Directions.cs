using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using RouteFilter.Components;
using RouteFilter.Persistence;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CarLane = Game.Net.CarLane;
using SubLane = Game.Net.SubLane;

namespace RouteFilter.Systems;

/// <summary>Derived from the same directed gates used by candidates. Never serialized.</summary>
internal sealed class LogicalEntryGroup
{
    internal RestrictionEntryIdentity Identity;
    internal Entity Connection;
    internal readonly List<DirectedEntryGate> Gates = new();
    internal bool Enabled = true;
    internal bool CustomSupported = true;
}

public sealed partial class RestrictionIndexSystem
{
    private RestrictionTopology m_EditorTopology = new();
    private Entity m_EditorTarget;
    private int m_EditorSourceRevision = -1;
    private readonly HashSet<Entity> m_DirectionWarnings = new();

    internal bool EntryEnabled(DirectedEntryGate gate) =>
        !m_Current.DisabledGatesByTarget.TryGetValue(gate.m_Target, out var disabled) || !disabled.Contains(gate);

    // Query setup consumes this already-derived set. No new per-frame topology walk.
    internal bool TryGetDirectionExclusion(Entity target, out IReadOnlyCollection<Entity> lanes)
    {
        lanes = null;
        if (!m_Current.DisabledGatesByTarget.TryGetValue(target, out var disabled) || disabled.Count == 0) return false;
        lanes = m_Current.DirectionLanesFor(target); return true;
    }

    internal IReadOnlyList<LogicalEntryGroup> GetEditorEntries(Entity target, bool force)
    {
        if (target == Entity.Null || !EntityManager.Exists(target)) return System.Array.Empty<LogicalEntryGroup>();
        if (!force && m_Current.EntryGroups.TryGetValue(target, out var current) && m_Current.ActiveTargets.Contains(target)) return current;
        if (force || target != m_EditorTarget || m_EditorSourceRevision != Revision)
        {
            if (target != m_EditorTarget) m_EditorTopology = new RestrictionTopology();
            m_EditorTarget = target; m_EditorSourceRevision = Revision;
            m_EditorTopology.Reset();
            var node = EntityManager.HasComponent<Node>(target);
            m_EditorTopology.TargetTypes[target] = node ? RestrictionTopologyTargetType.Node : RestrictionTopologyTargetType.Segment;
            if (node) BuildNodeTopology(m_EditorTopology, target); else BuildSegmentTopology(m_EditorTopology, target);
            BuildLogicalEntries(m_EditorTopology, target);
        }
        return m_EditorTopology.EntryGroups.TryGetValue(target, out var groups) ? groups : System.Array.Empty<LogicalEntryGroup>();
    }

    private void BuildLogicalEntries(RestrictionTopology topology, Entity target)
    {
        if (!topology.EntryGroups.TryGetValue(target, out var groups)) topology.EntryGroups[target] = groups = new();
        groups.Clear();
        var byIdentity = new Dictionary<RestrictionEntryIdentity, LogicalEntryGroup>();
        var supported = true;
        var reason = "";
        foreach (var gate in topology.GatesForTarget(target))
        {
            if (!TryDescribeEntry(gate, out var identity, out var connection)) { supported = false; reason = "EntryIdentityUnresolved"; continue; }
            if (!byIdentity.TryGetValue(identity, out var group))
            { group = new LogicalEntryGroup { Identity = identity, Connection = connection }; groups.Add(group); byIdentity.Add(identity, group); }
            else if (group.Connection != connection) { supported = false; reason = "EntryIdentityCollision"; }
            group.Gates.Add(gate);
        }
        supported &= groups.Count > 0 && groups.Count <= RouteFilterSaveData.MaxEntryCount;
        if (groups.Count == 0 || groups.Count > RouteFilterSaveData.MaxEntryCount) reason = "EmptyOrOversizedEntrySet";
        // If two approaches share a native graph edge, excluding that edge cannot faithfully
        // express one approach enabled and the other disabled. Never guess or over-delete.
        var graphOwners = new Dictionary<Entity, LogicalEntryGroup>();
        foreach (var group in groups)
            foreach (var gate in group.Gates)
            {
                var graphLane = NativeGraphLane(gate.m_NextLane);
                if (graphOwners.TryGetValue(graphLane, out var owner) && owner != group) { supported = false; reason = "SharedNativeGraphEdge"; }
                else graphOwners[graphLane] = group;
                if (EntityManager.TryGetComponent(gate.m_NextLane, out CarLane car) &&
                    (car.m_Flags & CarLaneFlags.Twoway) != 0) { supported = false; reason = "BidirectionalNativeGraphEdge"; }
                if (graphLane != gate.m_NextLane && EntityManager.TryGetComponent(graphLane, out CarLane masterCar) &&
                    (masterCar.m_Flags & CarLaneFlags.Twoway) != 0) { supported = false; reason = "BidirectionalMasterGraphEdge"; }
            }
        var entries = World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().GetDirectionIntent(target);
        if (entries != null)
            foreach (var identity in entries) if (!byIdentity.ContainsKey(identity)) { supported = false; reason = "PersistedEntryMissing"; }
        var enabled = entries == null ? null : new HashSet<RestrictionEntryIdentity>(entries);
        foreach (var group in groups)
        {
            group.CustomSupported = supported;
            group.Enabled = !supported || enabled == null || enabled.Contains(group.Identity);
            foreach (var gate in group.Gates)
                if (group.Enabled) topology.DirectionLanesFor(target).Add(gate.m_NextLane);
                else topology.DisabledGatesFor(target).Add(gate);
        }
        if (entries != null && !supported && m_DirectionWarnings.Add(target))
            Mod.Log.Warn($"[RouteFilter.Directions] target={target} reason={reason}: custom mapping unavailable, retaining all-entry behavior");
        else if (supported) m_DirectionWarnings.Remove(target);
    }

    private Entity NativeGraphLane(Entity lane)
    {
        if (EntityManager.TryGetComponent(lane, out SlaveLane slave) &&
            EntityManager.TryGetComponent(lane, out Owner owner) &&
            EntityManager.TryGetBuffer(owner.m_Owner, true, out DynamicBuffer<SubLane> sub) && slave.m_MasterIndex >= 0 && slave.m_MasterIndex < sub.Length &&
            EntityManager.HasComponent<MasterLane>(sub[slave.m_MasterIndex].m_SubLane))
            return sub[slave.m_MasterIndex].m_SubLane;
        return lane;
    }

    private bool TryDescribeEntry(DirectedEntryGate gate, out RestrictionEntryIdentity identity, out Entity connection)
    {
        identity = default;
        connection = gate.m_Target;
        Entity endpoint;
        if (gate.m_TargetEndpoint == RestrictionEndpoint.None)
        {
            if (!EntityManager.TryGetComponent(gate.m_EntryLane, out Owner owner) || !EntityManager.HasComponent<Edge>(owner.m_Owner)) return false;
            connection = owner.m_Owner; endpoint = gate.m_Target;
        }
        else
        {
            if (!EntityManager.TryGetComponent(connection, out Edge edge)) return false;
            endpoint = gate.m_TargetEndpoint == RestrictionEndpoint.Start ? edge.m_Start : edge.m_End;
        }
        var persistence = World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>();
        if (!persistence.TryDescribeTarget(connection, 1, out var road) ||
            !EntityManager.TryGetComponent(endpoint, out Node node) ||
            !EntityManager.TryGetComponent(connection, out Curve curve) ||
            !EntityManager.TryGetComponent(connection, out PrefabRef prefab)) return false;
        var mid = MathUtils.Position(curve.m_Bezier, .5f);
        identity = new RestrictionEntryIdentity { Connection = road,
            Endpoint = RestrictionAnchor.Quantize(node.m_Position.x, node.m_Position.y, node.m_Position.z),
            CurveMidpoint = RestrictionAnchor.Quantize(mid.x, mid.y, mid.z),
            RoadPrefab = World.GetOrCreateSystemManaged<PrefabSystem>().GetPrefabName(prefab.m_Prefab) };
        return identity.IsValid;
    }

    internal bool TryGetApproachGeometry(LogicalEntryGroup group, out float3 start, out float3 end)
    {
        var valid = TryGetApproachFrame(group, false, out var center, out var tangent, out var low, out var high);
        var normal = math.normalizesafe(new float3(-tangent.z, 0f, tangent.x));
        center.y += .25f;
        start = center + normal * low; end = center + normal * high;
        return valid;
    }

    internal bool TryGetApproachFrame(LogicalEntryGroup group, bool actualWidths, out float3 center,
        out float3 tangent, out float low, out float high)
    {
        center = tangent = float3.zero; low = high = 0f;
        var count = 0;
        if (group.Gates.Count == 0) return false;
        var seen = actualWidths ? new HashSet<Entity>() : null;
        var node = group.Gates[0].m_TargetEndpoint == RestrictionEndpoint.None;
        foreach (var gate in group.Gates)
        {
            var lane = node ? gate.m_EntryLane : gate.m_NextLane;
            if (seen != null && !seen.Add(lane)) continue;
            if (!EntityManager.TryGetComponent(lane, out Curve curve) || curve.m_Length <= .1f) return false;
            var forward = (node ? gate.m_EntryDirection : gate.m_NextDirection) == LaneTraversalDirection.Forward;
            var offset = math.min(.35f, 8f / curve.m_Length);
            var t = node ? (forward ? 1f - offset : offset) : (forward ? offset : 1f - offset);
            center += MathUtils.Position(curve.m_Bezier, t);
            tangent += MathUtils.Tangent(curve.m_Bezier, t) * (forward ? 1f : -1f); count++;
        }
        if (count == 0 || math.lengthsq(tangent.xz) < .01f) return false;
        center /= count;
        var normal = math.normalizesafe(new float3(-tangent.z, 0f, tangent.x));
        low = float.MaxValue; high = float.MinValue;
        seen?.Clear();
        foreach (var gate in group.Gates)
        {
            var lane = node ? gate.m_EntryLane : gate.m_NextLane;
            if (seen != null && !seen.Add(lane)) continue;
            var curve = EntityManager.GetComponentData<Curve>(lane);
            var forward = (node ? gate.m_EntryDirection : gate.m_NextDirection) == LaneTraversalDirection.Forward;
            var offset = math.min(.35f, 8f / curve.m_Length);
            var t = node ? (forward ? 1f - offset : offset) : (forward ? offset : 1f - offset);
            var lateral = math.dot(MathUtils.Position(curve.m_Bezier, t) - center, normal);
            var halfWidth = 1.6f;
            if (actualWidths)
            {
                if (!EntityManager.TryGetComponent(lane, out PrefabRef prefab) ||
                    !EntityManager.TryGetComponent(prefab.m_Prefab, out NetLaneData laneData) ||
                    !math.isfinite(laneData.m_Width) || laneData.m_Width <= .1f) return false;
                halfWidth = laneData.m_Width * .5f;
            }
            low = math.min(low, lateral - halfWidth); high = math.max(high, lateral + halfWidth);
        }
        tangent = math.normalizesafe(new float3(tangent.x, 0f, tangent.z));
        return true;
    }

    internal IReadOnlyList<LogicalEntryGroup> GetAppliedRoadEntries(Entity target)
    {
        if (target == Entity.Null || !EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target) ||
            !EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets))
            return System.Array.Empty<LogicalEntryGroup>();
        var road = false;
        foreach (var asset in assets) road |= EntityManager.HasComponent<CarData>(asset.m_Prefab);
        if (!road) return System.Array.Empty<LogicalEntryGroup>();
        var topology = new RestrictionTopology();
        if (EntityManager.HasComponent<Node>(target)) BuildNodeTopology(topology, target);
        else if (EntityManager.HasComponent<Edge>(target)) BuildSegmentTopology(topology, target);
        else return System.Array.Empty<LogicalEntryGroup>();
        BuildLogicalEntries(topology, target);
        return topology.EntryGroups.TryGetValue(target, out var groups) ? groups : System.Array.Empty<LogicalEntryGroup>();
    }
}
