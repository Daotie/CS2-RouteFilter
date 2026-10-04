using Game.Pathfind;
using RouteFilter.Components;
using System.Collections.Generic;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>Managed build-stage storage for immutable-at-runtime topology snapshots.</summary>
internal sealed class RestrictionTopology
{
    internal readonly HashSet<Entity> ActiveTargets = new();
    internal readonly HashSet<Entity> RestrictedPrefabs = new();
    internal readonly Dictionary<Entity, RestrictionTopologyTargetType> TargetTypes = new();
    internal readonly Dictionary<Entity, HashSet<Entity>> TargetPrefabs = new();
    internal readonly Dictionary<Entity, HashSet<DirectedEntryGate>> GatesByEntryLane = new();
    internal readonly Dictionary<Entity, HashSet<DirectedEntryGate>> GatesByTarget = new();
    internal readonly Dictionary<Entity, HashSet<Entity>> InternalLanesByTarget = new();
    internal readonly Dictionary<Entity, HashSet<Entity>> OutboundLanesByTarget = new();
    internal readonly Dictionary<Entity, HashSet<RestrictionTopologyAmbiguity>> AmbiguitiesByTarget = new();
    internal readonly Dictionary<Entity, List<LogicalEntryGroup>> EntryGroups = new();
    internal readonly Dictionary<Entity, HashSet<DirectedEntryGate>> DisabledGatesByTarget = new();
    internal readonly Dictionary<Entity, HashSet<Entity>> DirectionExclusionLanes = new();

    internal void Reset()
    {
        ActiveTargets.Clear();
        RestrictedPrefabs.Clear();
        TargetTypes.Clear();
        ClearValues(TargetPrefabs);
        ClearValues(GatesByEntryLane);
        ClearValues(GatesByTarget);
        ClearValues(InternalLanesByTarget);
        ClearValues(OutboundLanesByTarget);
        ClearValues(AmbiguitiesByTarget);
        EntryGroups.Clear();
        DisabledGatesByTarget.Clear();
        DirectionExclusionLanes.Clear();
    }

    internal HashSet<Entity> PrefabsFor(Entity target) => GetSet(TargetPrefabs, target);
    internal HashSet<DirectedEntryGate> GatesForTarget(Entity target) => GetSet(GatesByTarget, target);
    internal HashSet<DirectedEntryGate> GatesForEntry(Entity lane) => GetSet(GatesByEntryLane, lane);
    internal HashSet<Entity> InternalLanesFor(Entity target) => GetSet(InternalLanesByTarget, target);
    internal HashSet<Entity> OutboundLanesFor(Entity target) => GetSet(OutboundLanesByTarget, target);
    internal HashSet<RestrictionTopologyAmbiguity> AmbiguitiesFor(Entity target) => GetSet(AmbiguitiesByTarget, target);
    internal HashSet<DirectedEntryGate> DisabledGatesFor(Entity target) => GetSet(DisabledGatesByTarget, target);
    internal HashSet<Entity> DirectionLanesFor(Entity target) => GetSet(DirectionExclusionLanes, target);

    internal bool ContentEquals(RestrictionTopology other)
    {
        if (!ActiveTargets.SetEquals(other.ActiveTargets) ||
            !RestrictedPrefabs.SetEquals(other.RestrictedPrefabs) ||
            TargetTypes.Count != other.TargetTypes.Count)
            return false;

        foreach (var pair in TargetTypes)
            if (!other.TargetTypes.TryGetValue(pair.Key, out var value) || value != pair.Value)
                return false;

        return DictionarySetsEqual(TargetPrefabs, other.TargetPrefabs, ActiveTargets) &&
               DictionarySetsEqual(GatesByTarget, other.GatesByTarget, ActiveTargets) &&
               DictionarySetsEqual(InternalLanesByTarget, other.InternalLanesByTarget, ActiveTargets) &&
               DictionarySetsEqual(OutboundLanesByTarget, other.OutboundLanesByTarget, ActiveTargets) &&
               DictionarySetsEqual(AmbiguitiesByTarget, other.AmbiguitiesByTarget, ActiveTargets) &&
               DictionarySetsEqual(DisabledGatesByTarget, other.DisabledGatesByTarget, ActiveTargets) &&
               EntryGroupsEqual(other) &&
               EntryGateMapsEqual(other);
    }

    private bool EntryGroupsEqual(RestrictionTopology other)
    {
        foreach (var target in ActiveTargets)
        {
            if (!EntryGroups.TryGetValue(target, out var groups) || !other.EntryGroups.TryGetValue(target, out var peers) || groups.Count != peers.Count) return false;
            foreach (var group in groups)
            {
                var found = false;
                foreach (var peer in peers)
                    if (group.Identity.Equals(peer.Identity) && group.Enabled == peer.Enabled && group.CustomSupported == peer.CustomSupported) { found = true; break; }
                if (!found) return false;
            }
        }
        return true;
    }

    private bool EntryGateMapsEqual(RestrictionTopology other)
    {
        foreach (var pair in GatesByEntryLane)
        {
            if (pair.Value.Count == 0) continue;
            if (!other.GatesByEntryLane.TryGetValue(pair.Key, out var otherSet) || !pair.Value.SetEquals(otherSet))
                return false;
        }
        foreach (var pair in other.GatesByEntryLane)
        {
            if (pair.Value.Count == 0) continue;
            if (!GatesByEntryLane.TryGetValue(pair.Key, out var set) || !pair.Value.SetEquals(set))
                return false;
        }
        return true;
    }

    private static bool DictionarySetsEqual<T>(
        Dictionary<Entity, HashSet<T>> left,
        Dictionary<Entity, HashSet<T>> right,
        HashSet<Entity> activeTargets)
    {
        foreach (var target in activeTargets)
        {
            left.TryGetValue(target, out var leftSet);
            right.TryGetValue(target, out var rightSet);
            var leftCount = leftSet?.Count ?? 0;
            var rightCount = rightSet?.Count ?? 0;
            if (leftCount != rightCount || (leftCount != 0 && !leftSet!.SetEquals(rightSet!)))
                return false;
        }
        return true;
    }

    private static HashSet<T> GetSet<T>(Dictionary<Entity, HashSet<T>> dictionary, Entity key)
    {
        if (!dictionary.TryGetValue(key, out var set))
        {
            set = new HashSet<T>();
            dictionary.Add(key, set);
        }
        return set;
    }

    private static void ClearValues<T>(Dictionary<Entity, HashSet<T>> dictionary)
    {
        foreach (var set in dictionary.Values) set.Clear();
    }
}

/// <summary>A single usable direction through one CarLane entity.</summary>
internal readonly struct DirectedLaneTraversal
{
    internal readonly Entity Lane;
    internal readonly Entity Owner;
    internal readonly PathNode From;
    internal readonly PathNode To;
    internal readonly LaneTraversalDirection Direction;

    internal DirectedLaneTraversal(
        Entity lane,
        Entity owner,
        PathNode from,
        PathNode to,
        LaneTraversalDirection direction)
    {
        Lane = lane;
        Owner = owner;
        From = from;
        To = to;
        Direction = direction;
    }
}
