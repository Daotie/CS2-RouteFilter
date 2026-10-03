using Colossal.Entities;
using Game;
using Game.Net;
using RouteFilter.Components;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Builds the directed, target-specific road topology used by RouteFilter 2.0. The build
/// stage is managed and infrequent; its records contain only Entities and compact enums so
/// Phase 1B can copy them directly into Burst-friendly native collections.
/// </summary>
public sealed partial class RestrictionIndexSystem : GameSystemBase
{
    private const int kPeriodicRefreshTicks = 1024;

    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private RestrictionTopology m_Current = new();
    private RestrictionTopology m_Scratch = new();
    private readonly List<DirectedLaneTraversal> m_InternalTraversals = new(64);
    private readonly List<DirectedLaneTraversal> m_AdjacentTraversals = new(128);
    private readonly HashSet<Entity> m_AdjacentOwners = new();
    private int m_RefreshTicks = kPeriodicRefreshTicks;

    /// <summary>Increments only when the logical restriction topology really changes.</summary>
    public int Revision { get; private set; }

    /// <summary>Compatibility name for old consumers. It has the same strict semantics.</summary>
    public int Version => Revision;

    public IReadOnlyCollection<Entity> RestrictedPrefabs => m_Current.RestrictedPrefabs;

    public IEnumerable<KeyValuePair<Entity, IReadOnlyCollection<Entity>>> TargetPrefabs
    {
        get
        {
            foreach (var target in m_Current.ActiveTargets)
                yield return new KeyValuePair<Entity, IReadOnlyCollection<Entity>>(
                    target,
                    m_Current.PrefabsFor(target));
        }
    }

    public IEnumerable<KeyValuePair<Entity, IReadOnlyCollection<DirectedEntryGate>>> DirectedGatesByEntryLane
    {
        get
        {
            foreach (var pair in m_Current.GatesByEntryLane)
                if (pair.Value.Count != 0)
                    yield return new KeyValuePair<Entity, IReadOnlyCollection<DirectedEntryGate>>(pair.Key, pair.Value);
        }
    }

    public IEnumerable<KeyValuePair<Entity, IReadOnlyCollection<DirectedEntryGate>>> TargetDirectedGates
    {
        get
        {
            foreach (var target in m_Current.ActiveTargets)
                yield return new KeyValuePair<Entity, IReadOnlyCollection<DirectedEntryGate>>(
                    target,
                    m_Current.GatesForTarget(target));
        }
    }

    public IEnumerable<KeyValuePair<Entity, IReadOnlyCollection<Entity>>> TargetInternalLanes
    {
        get
        {
            foreach (var target in m_Current.ActiveTargets)
                yield return new KeyValuePair<Entity, IReadOnlyCollection<Entity>>(
                    target,
                    m_Current.InternalLanesFor(target));
        }
    }

    public IEnumerable<KeyValuePair<Entity, IReadOnlyCollection<Entity>>> TargetOutboundLanes
    {
        get
        {
            foreach (var target in m_Current.ActiveTargets)
                yield return new KeyValuePair<Entity, IReadOnlyCollection<Entity>>(
                    target,
                    m_Current.OutboundLanesFor(target));
        }
    }

    public IEnumerable<KeyValuePair<Entity, IReadOnlyCollection<RestrictionTopologyAmbiguity>>> TargetAmbiguities
    {
        get
        {
            foreach (var target in m_Current.ActiveTargets)
                yield return new KeyValuePair<Entity, IReadOnlyCollection<RestrictionTopologyAmbiguity>>(
                    target,
                    m_Current.AmbiguitiesFor(target));
        }
    }

    public bool ContainsRestrictedPrefab(Entity prefab) => m_Current.RestrictedPrefabs.Contains(prefab);

    /// <summary>Number of targets with a non-empty forbidden list. Zero means the fast path.</summary>
    public int ActiveTargetCount => m_Current.ActiveTargets.Count;

    /// <summary>Gate records across all active targets. O(1).</summary>
    public int ActiveGateCount => m_Current.GatesByEntryLane.Count;

    public bool TargetRestricts(Entity target, IReadOnlyCollection<Entity> prefabs)
    {
        if (target == Entity.Null || !m_Current.ActiveTargets.Contains(target) ||
            !m_Current.TargetPrefabs.TryGetValue(target, out var restricted))
            return false;

        foreach (var prefab in prefabs)
            if (restricted.Contains(prefab)) return true;
        return false;
    }

    public bool TryGetDirectedGates(Entity entryLane, out IReadOnlyCollection<DirectedEntryGate> gates)
    {
        if (m_Current.GatesByEntryLane.TryGetValue(entryLane, out var set) && set.Count != 0)
        {
            gates = set;
            return true;
        }
        gates = default!;
        return false;
    }

    public bool TryGetTargetGates(Entity target, out IReadOnlyCollection<DirectedEntryGate> gates)
    {
        if (m_Current.ActiveTargets.Contains(target) &&
            m_Current.GatesByTarget.TryGetValue(target, out var set) && set.Count != 0)
        {
            gates = set;
            return true;
        }
        gates = default!;
        return false;
    }

    public bool TryGetInternalLanes(Entity target, out IReadOnlyCollection<Entity> lanes)
    {
        if (m_Current.ActiveTargets.Contains(target) &&
            m_Current.InternalLanesByTarget.TryGetValue(target, out var set) && set.Count != 0)
        {
            lanes = set;
            return true;
        }
        lanes = default!;
        return false;
    }

    public bool TryGetTargetType(Entity target, out RestrictionTopologyTargetType targetType)
        => m_Current.TargetTypes.TryGetValue(target, out targetType);

    public void ResetRuntimeState()
    {
        m_Current.Reset();
        m_Scratch.Reset();
        m_InternalTraversals.Clear();
        m_AdjacentTraversals.Clear();
        m_AdjacentOwners.Clear();
        m_RefreshTicks = kPeriodicRefreshTicks;
        Revision++;
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
    }

    protected override void OnUpdate()
    {
        // Empty city configuration: cached query emptiness, no topology snapshots/arrays/logs.
        if (m_Current.ActiveTargets.Count == 0 && m_RestrictedNodes.IsEmptyIgnoreFilter &&
            m_RestrictedSegments.IsEmptyIgnoreFilter)
        {
            Mod.RestrictionsDirty = false;
            m_RefreshTicks = 0;
            return;
        }
        if (!Mod.RestrictionsDirty && ++m_RefreshTicks < kPeriodicRefreshTicks) return;

        Mod.RestrictionsDirty = false;
        m_RefreshTicks = 0;
        BuildTopology(m_Scratch);

        if (m_Current.ContentEquals(m_Scratch)) return;

        var previous = m_Current;
        m_Current = m_Scratch;
        m_Scratch = previous;
        Revision++;
        DumpTopologyToLog();
    }

    private void BuildTopology(RestrictionTopology topology)
    {
        topology.Reset();

        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        foreach (var node in nodes)
            AddRestrictedTarget(topology, node, RestrictionTopologyTargetType.Node);

        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        foreach (var segment in segments)
            AddRestrictedTarget(topology, segment, RestrictionTopologyTargetType.Segment);
    }

    private void AddRestrictedTarget(
        RestrictionTopology topology,
        Entity target,
        RestrictionTopologyTargetType targetType)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets) ||
            assets.Length == 0)
            return;

        var targetPrefabs = topology.PrefabsFor(target);
        foreach (var asset in assets)
        {
            if (asset.m_Prefab == Entity.Null || !targetPrefabs.Add(asset.m_Prefab)) continue;
            topology.RestrictedPrefabs.Add(asset.m_Prefab);
        }

        if (targetPrefabs.Count == 0) return;

        topology.ActiveTargets.Add(target);
        topology.TargetTypes[target] = targetType;

        if (targetType == RestrictionTopologyTargetType.Node)
            BuildNodeTopology(topology, target);
        else
            BuildSegmentTopology(topology, target);
    }

    private void BuildNodeTopology(RestrictionTopology topology, Entity node)
    {
        m_InternalTraversals.Clear();
        m_AdjacentTraversals.Clear();
        m_AdjacentOwners.Clear();

        AddOwnerTraversals(topology, node, node, m_InternalTraversals);

        if (EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
        {
            foreach (var connected in edges)
            {
                if (connected.m_Edge == Entity.Null || !m_AdjacentOwners.Add(connected.m_Edge)) continue;
                AddOwnerTraversals(topology, node, connected.m_Edge, m_AdjacentTraversals);
            }
        }
        else
        {
            AddAmbiguity(topology, node, node, Entity.Null,
                RestrictionTopologyAmbiguityReason.MissingSubLanes);
        }

        var internalLanes = topology.InternalLanesFor(node);
        foreach (var traversal in m_InternalTraversals) internalLanes.Add(traversal.Lane);

        // Direct edge-to-edge paths can coexist with node-owned connector lanes.
        // Its adjacent edge lanes join directly on the SAME native PathNode at this node.
        // Only exact directed connections qualify; adjacency alone never creates a gate.
        foreach (var inbound in m_AdjacentTraversals)
        foreach (var outbound in m_AdjacentTraversals)
        {
            if (inbound.Owner == outbound.Owner || inbound.To.GetOwnerIndex() != node.Index ||
                !inbound.To.Equals(outbound.From)) continue;
            AddGate(topology, new DirectedEntryGate { m_EntryLane = inbound.Lane,
                m_NextLane = outbound.Lane, m_Target = node, m_EntryDirection = inbound.Direction,
                m_NextDirection = outbound.Direction, m_TargetEndpoint = RestrictionEndpoint.None });
            // Query-local exclusion of the outbound edge prevents this directed crossing.
            // It never changes the physical lane or another vehicle's query.
            internalLanes.Add(outbound.Lane);
            topology.OutboundLanesFor(node).Add(outbound.Lane);
        }
        if (internalLanes.Count == 0)
            AddAmbiguity(topology, node, node, Entity.Null,
                RestrictionTopologyAmbiguityReason.NoRoadInternalLanes);

        foreach (var internalTraversal in m_InternalTraversals)
        {
            var hasInbound = false;
            var hasInternalPredecessor = false;
            var hasOutbound = false;
            var hasInternalSuccessor = false;

            foreach (var adjacent in m_AdjacentTraversals)
            {
                if (adjacent.To.Equals(internalTraversal.From))
                {
                    hasInbound = true;
                    AddGate(topology, new DirectedEntryGate
                    {
                        m_EntryLane = adjacent.Lane,
                        m_NextLane = internalTraversal.Lane,
                        m_Target = node,
                        m_EntryDirection = adjacent.Direction,
                        m_NextDirection = internalTraversal.Direction,
                        m_TargetEndpoint = RestrictionEndpoint.None
                    });
                }

                if (internalTraversal.To.Equals(adjacent.From))
                {
                    hasOutbound = true;
                    topology.OutboundLanesFor(node).Add(adjacent.Lane);
                }
            }

            foreach (var otherInternal in m_InternalTraversals)
            {
                if (otherInternal.Lane == internalTraversal.Lane &&
                    otherInternal.Direction == internalTraversal.Direction)
                    continue;

                if (otherInternal.To.Equals(internalTraversal.From)) hasInternalPredecessor = true;
                if (internalTraversal.To.Equals(otherInternal.From)) hasInternalSuccessor = true;
            }

            if (!hasInbound && !hasInternalPredecessor)
                AddAmbiguity(topology, node, internalTraversal.Lane, Entity.Null,
                    RestrictionTopologyAmbiguityReason.NoInboundTraversal);
            if (!hasOutbound && !hasInternalSuccessor)
                AddAmbiguity(topology, node, internalTraversal.Lane, Entity.Null,
                    RestrictionTopologyAmbiguityReason.NoOutboundTraversal);
        }

        if (topology.GatesForTarget(node).Count == 0)
            AddAmbiguity(topology, node, node, Entity.Null,
                RestrictionTopologyAmbiguityReason.NoDirectedEntryGate);
    }

    private void BuildSegmentTopology(RestrictionTopology topology, Entity segment)
    {
        m_InternalTraversals.Clear();
        m_AdjacentTraversals.Clear();
        m_AdjacentOwners.Clear();

        AddOwnerTraversals(topology, segment, segment, m_InternalTraversals);
        var edge = EntityManager.GetComponentData<Edge>(segment);
        if (edge.m_Start != Entity.Null && m_AdjacentOwners.Add(edge.m_Start))
            AddOwnerTraversals(topology, segment, edge.m_Start, m_AdjacentTraversals);
        if (edge.m_End != Entity.Null && m_AdjacentOwners.Add(edge.m_End))
            AddOwnerTraversals(topology, segment, edge.m_End, m_AdjacentTraversals);

        var internalLanes = topology.InternalLanesFor(segment);
        foreach (var traversal in m_InternalTraversals) internalLanes.Add(traversal.Lane);

        if (internalLanes.Count == 0)
            AddAmbiguity(topology, segment, segment, Entity.Null,
                RestrictionTopologyAmbiguityReason.NoRoadInternalLanes);

        foreach (var internalTraversal in m_InternalTraversals)
        {
            var hasInbound = false;
            var hasInternalPredecessor = false;
            var hasOutbound = false;
            var hasInternalSuccessor = false;

            foreach (var adjacent in m_AdjacentTraversals)
            {
                if (adjacent.To.Equals(internalTraversal.From))
                {
                    var endpoint = GetEndpoint(edge, adjacent.Owner);
                    if (endpoint == RestrictionEndpoint.None)
                    {
                        AddAmbiguity(topology, segment, internalTraversal.Lane, adjacent.Lane,
                            RestrictionTopologyAmbiguityReason.EndpointOwnerMismatch);
                    }
                    else
                    {
                        hasInbound = true;
                        AddGate(topology, new DirectedEntryGate
                        {
                            m_EntryLane = adjacent.Lane,
                            m_NextLane = internalTraversal.Lane,
                            m_Target = segment,
                            m_EntryDirection = adjacent.Direction,
                            m_NextDirection = internalTraversal.Direction,
                            m_TargetEndpoint = endpoint
                        });
                    }
                }

                if (internalTraversal.To.Equals(adjacent.From))
                {
                    hasOutbound = true;
                    topology.OutboundLanesFor(segment).Add(adjacent.Lane);
                }
            }

            foreach (var otherInternal in m_InternalTraversals)
            {
                if (otherInternal.Lane == internalTraversal.Lane &&
                    otherInternal.Direction == internalTraversal.Direction)
                    continue;

                if (otherInternal.To.Equals(internalTraversal.From)) hasInternalPredecessor = true;
                if (internalTraversal.To.Equals(otherInternal.From)) hasInternalSuccessor = true;
            }

            if (!hasInbound && !hasInternalPredecessor)
                AddAmbiguity(topology, segment, internalTraversal.Lane, Entity.Null,
                    RestrictionTopologyAmbiguityReason.NoInboundTraversal);
            if (!hasOutbound && !hasInternalSuccessor)
                AddAmbiguity(topology, segment, internalTraversal.Lane, Entity.Null,
                    RestrictionTopologyAmbiguityReason.NoOutboundTraversal);
        }

        if (topology.GatesForTarget(segment).Count == 0)
            AddAmbiguity(topology, segment, segment, Entity.Null,
                RestrictionTopologyAmbiguityReason.NoDirectedEntryGate);
    }

    private void AddOwnerTraversals(
        RestrictionTopology topology,
        Entity diagnosticTarget,
        Entity owner,
        List<DirectedLaneTraversal> output)
    {
        if (!EntityManager.TryGetBuffer(owner, true, out DynamicBuffer<SubLane> subLanes))
        {
            AddAmbiguity(topology, diagnosticTarget, owner, Entity.Null,
                RestrictionTopologyAmbiguityReason.MissingSubLanes);
            return;
        }

        foreach (var subLane in subLanes)
        {
            var laneEntity = subLane.m_SubLane;
            if (laneEntity == Entity.Null || !EntityManager.HasComponent<CarLane>(laneEntity)) continue;
            if (!EntityManager.TryGetComponent(laneEntity, out Lane lane))
            {
                AddAmbiguity(topology, diagnosticTarget, laneEntity, owner,
                    RestrictionTopologyAmbiguityReason.MissingLaneData);
                continue;
            }

            // Vanilla IsContinuous resolves SlaveLane to its owner's master before
            // comparing connection nodes. Physical slaves do not necessarily share
            // the master's exact PathNode keys, but carry the actual LaneObjects.
            if (EntityManager.TryGetComponent(laneEntity, out SlaveLane slave) &&
                slave.m_MasterIndex < subLanes.Length)
            {
                var masterEntity = subLanes[slave.m_MasterIndex].m_SubLane;
                if (EntityManager.HasComponent<MasterLane>(masterEntity) &&
                    EntityManager.TryGetComponent(masterEntity, out Lane masterConnection)) lane = masterConnection;
            }
            if (lane.m_StartNode.Equals(lane.m_EndNode))
            {
                AddAmbiguity(topology, diagnosticTarget, laneEntity, owner,
                    RestrictionTopologyAmbiguityReason.DegenerateTraversal);
                continue;
            }

            output.Add(new DirectedLaneTraversal(
                laneEntity,
                owner,
                lane.m_StartNode,
                lane.m_EndNode,
                LaneTraversalDirection.Forward));

            var carLane = EntityManager.GetComponentData<CarLane>(laneEntity);
            if ((carLane.m_Flags & CarLaneFlags.Twoway) != 0)
            {
                output.Add(new DirectedLaneTraversal(
                    laneEntity,
                    owner,
                    lane.m_EndNode,
                    lane.m_StartNode,
                    LaneTraversalDirection.Reverse));
            }
        }
    }

    internal bool TryGetConnectionLane(Entity entity, out Lane lane)
    {
        if (!EntityManager.TryGetComponent(entity, out lane)) return false;
        if (EntityManager.TryGetComponent(entity, out SlaveLane slave) &&
            EntityManager.TryGetComponent(entity, out Game.Common.Owner owner) &&
            EntityManager.TryGetBuffer(owner.m_Owner, true, out DynamicBuffer<SubLane> lanes) &&
            slave.m_MasterIndex < lanes.Length)
        {
            var master = lanes[slave.m_MasterIndex].m_SubLane;
            if (EntityManager.HasComponent<MasterLane>(master) && EntityManager.TryGetComponent(master, out Lane connection))
                lane = connection;
        }
        return true;
    }

    private static RestrictionEndpoint GetEndpoint(Edge edge, Entity owner)
    {
        if (owner == Entity.Null || edge.m_Start == edge.m_End) return RestrictionEndpoint.None;
        if (owner == edge.m_Start) return RestrictionEndpoint.Start;
        if (owner == edge.m_End) return RestrictionEndpoint.End;
        return RestrictionEndpoint.None;
    }

    private static void AddGate(RestrictionTopology topology, DirectedEntryGate gate)
    {
        if (gate.m_EntryLane == Entity.Null || gate.m_NextLane == Entity.Null ||
            gate.m_EntryLane == gate.m_NextLane)
        {
            AddAmbiguity(topology, gate.m_Target, gate.m_EntryLane, gate.m_NextLane,
                RestrictionTopologyAmbiguityReason.SelfReferentialGate);
            return;
        }

        topology.GatesForTarget(gate.m_Target).Add(gate);
        topology.GatesForEntry(gate.m_EntryLane).Add(gate);
    }

    private static void AddAmbiguity(
        RestrictionTopology topology,
        Entity target,
        Entity lane,
        Entity relatedLane,
        RestrictionTopologyAmbiguityReason reason)
    {
        topology.AmbiguitiesFor(target).Add(new RestrictionTopologyAmbiguity
        {
            m_Target = target,
            m_Lane = lane,
            m_RelatedLane = relatedLane,
            m_Reason = reason
        });
    }

    /// <summary>Builds a deterministic, human-readable dump for in-game topology checks.</summary>
    public string BuildTopologyDump(Entity target = default)
    {
        var builder = new StringBuilder(2048);
        var targets = target == Entity.Null
            ? m_Current.ActiveTargets.OrderBy(EntitySortKey).ToArray()
            : new[] { target };

        builder.Append("[RouteFilter.Topology] Revision ").Append(Revision).AppendLine();
        foreach (var item in targets)
        {
            if (!m_Current.ActiveTargets.Contains(item))
            {
                builder.Append("Target ").Append(FormatEntity(item)).AppendLine(" is not indexed");
                continue;
            }

            var type = m_Current.TargetTypes[item];
            var prefabs = m_Current.PrefabsFor(item);
            var gates = m_Current.GatesForTarget(item)
                .OrderBy(g => EntitySortKey(g.m_EntryLane))
                .ThenBy(g => EntitySortKey(g.m_NextLane))
                .ThenBy(g => g.m_EntryDirection)
                .ThenBy(g => g.m_NextDirection);
            var internalLanes = m_Current.InternalLanesFor(item).OrderBy(EntitySortKey);
            var outboundLanes = m_Current.OutboundLanesFor(item).OrderBy(EntitySortKey);
            var ambiguities = m_Current.AmbiguitiesFor(item)
                .OrderBy(a => EntitySortKey(a.m_Lane))
                .ThenBy(a => a.m_Reason);

            builder.Append("Target Entity: ").Append(FormatEntity(item)).AppendLine();
            builder.Append("Target Type: ").Append(type).AppendLine();
            builder.Append("Forbidden Prefab Count: ").Append(prefabs.Count).AppendLine();
            builder.AppendLine("Directed Gates:");
            foreach (var gate in gates)
            {
                builder.Append("- Entry Lane ").Append(FormatEntity(gate.m_EntryLane))
                    .Append(" -> Internal/Next Lane ").Append(FormatEntity(gate.m_NextLane))
                    .Append("; EntryDirection=").Append(gate.m_EntryDirection)
                    .Append("; NextDirection=").Append(gate.m_NextDirection)
                    .Append("; Endpoint=").Append(gate.m_TargetEndpoint)
                    .Append("; Target=").Append(FormatEntity(gate.m_Target)).AppendLine();
            }
            builder.AppendLine("Internal Lanes:");
            foreach (var lane in internalLanes)
                builder.Append("- Lane Entity ").Append(FormatEntity(lane)).AppendLine();
            builder.AppendLine("Outbound Lanes:");
            foreach (var lane in outboundLanes)
                builder.Append("- Lane Entity ").Append(FormatEntity(lane)).AppendLine();
            builder.AppendLine("Ambiguous/Unsupported:");
            foreach (var ambiguity in ambiguities)
            {
                builder.Append("- Lane ").Append(FormatEntity(ambiguity.m_Lane))
                    .Append("; Related=").Append(FormatEntity(ambiguity.m_RelatedLane))
                    .Append("; Reason=").Append(ambiguity.m_Reason).AppendLine();
            }
            if (type == RestrictionTopologyTargetType.Node && !gates.Any() &&
                EntityManager.TryGetBuffer(item, true, out DynamicBuffer<ConnectedEdge> connected))
            {
                builder.AppendLine("Local connection evidence (bounded, only this target):");
                var rows = 0;
                foreach (var edge in connected)
                {
                    if (!EntityManager.TryGetBuffer(edge.m_Edge, true, out DynamicBuffer<SubLane> lanes)) continue;
                    foreach (var sub in lanes)
                    {
                        if (rows >= 64 || !EntityManager.HasComponent<CarLane>(sub.m_SubLane) ||
                            !TryGetConnectionLane(sub.m_SubLane, out Lane lane)) continue;
                        rows++;
                        builder.Append("- lane=").Append(FormatEntity(sub.m_SubLane)).Append(" owner=").Append(FormatEntity(edge.m_Edge))
                            .Append(" from=").Append(lane.m_StartNode.GetOwnerIndex()).Append('/').Append(lane.m_StartNode.GetLaneIndex())
                            .Append('/').Append(lane.m_StartNode.GetCurvePos()).Append(" to=").Append(lane.m_EndNode.GetOwnerIndex())
                            .Append('/').Append(lane.m_EndNode.GetLaneIndex()).Append('/').Append(lane.m_EndNode.GetCurvePos()).AppendLine();
                    }
                }
            }
        }
        return builder.ToString();
    }

    [Conditional("DEBUG")]
    private void DumpTopologyToLog() => Mod.Log.Info(BuildTopologyDump());

    private static long EntitySortKey(Entity entity)
        => ((long)entity.Index << 32) | (uint)entity.Version;

    private static string FormatEntity(Entity entity)
        => entity == Entity.Null ? "Null" : $"{entity.Index}:{entity.Version}";
}
