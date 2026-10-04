using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Tools;
using Game.Net;
using Game.Prefabs;
using RouteFilter.Components;
using RouteFilter.Persistence;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilter.Systems;

/// <summary>
/// Owns the only data RouteFilter puts in a save file: "these prefabs are forbidden at these
/// targets". Everything else - topology, candidates, safety verdicts, leases, attempts, graph
/// publication state, diagnostics - is derived at runtime and rebuilt from this configuration.
///
/// Schema bodies are bounded by a length and an integrity checksum (schema 4). Future/corrupt
/// bodies are retained in full. The game's native outer framing and legacy entity remapping
/// require live integration validation; offline fixtures do not prove either.
/// </summary>
public sealed partial class RestrictionPersistenceSystem : GameSystemBase, IDefaultSerializable
{
    private const int RestorePasses = 3;
    private int m_RestoreCursor;
    private long m_RestoreVisitsRemaining;

    private sealed class IntentRecord
    {
        public RestrictionTargetIdentity Identity;
        public Entity LegacyTarget;
        public byte LegacyKind;
        public bool IsLegacy;
        public Entity ResolvedTarget;
        public readonly HashSet<string> AssetNames = new();
        public readonly HashSet<string> ResolvedNames = new();
        public int Attempts;
        public RestrictionEntryIdentity[] EnabledEntries;
    }

    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private EntityQuery m_VehiclePrefabQuery;
    private EntityQuery m_NodeQuery;
    private PrefabSystem m_PrefabSystem = null!;

    private readonly List<IntentRecord> m_IntentRecord = new();
    private readonly Dictionary<string, Entity> m_PrefabEntitiesByName = new();
    private readonly Dictionary<RestrictionAnchor, Entity> m_NodeIndex = new();
    private readonly Dictionary<Entity, RestrictionEntryIdentity[]> m_DirectionIntent = new();
    private readonly List<Entity> m_ResolvedAssets = new();

    private bool m_NameMapStale = true;
    private bool m_NodeIndexBuilt;
    private byte[] m_ForeignPayload;
    private bool m_PersistenceLocked;
    private string m_LockReason = string.Empty;

    /// <summary>True while the payload must be re-emitted byte for byte instead of re-encoded.</summary>
    public bool PersistenceLocked => m_PersistenceLocked;

    /// <summary>True when the stored payload cannot be trusted, so enforcement must stay off.</summary>
    public bool DataTrusted { get; private set; } = true;
    public bool ConfigurationEditable => DataTrusted && !m_PersistenceLocked;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_PrefabSystem.onContentAvailabilityChanged += OnContentAvailabilityChanged;
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
        m_VehiclePrefabQuery = GetEntityQuery(
            ComponentType.ReadOnly<VehicleData>(),
            ComponentType.ReadOnly<PrefabData>());
        m_NodeQuery = GetEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
    }

    protected override void OnDestroy()
    {
        m_PrefabSystem.onContentAvailabilityChanged -= OnContentAvailabilityChanged;
        base.OnDestroy();
    }

    private void OnContentAvailabilityChanged()
    {
        m_NameMapStale = true;
        foreach (var pending in m_IntentRecord) { pending.Attempts = 0; pending.ResolvedNames.Clear(); }
        m_RestoreVisitsRemaining = (long)m_IntentRecord.Count * RestorePasses;
        Mod.Log.Info("[RouteFilter.Persistence] Content availability changed; prefab name map will be rebuilt");
    }

    protected override void OnUpdate()
    {
        if (!ConfigurationEditable || m_IntentRecord.Count == 0 || m_RestoreVisitsRemaining == 0) return;
        TryRestorePendingRestrictions();
    }

    public void SetDefaults(Context context)
    {
        ClearTransientState();
        m_PersistenceLocked = false;
        m_LockReason = string.Empty;
        DataTrusted = true;
    }

    public void ResetRuntimeState()
    {
        ClearTransientState();
        m_NameMapStale = true;
        m_NodeIndexBuilt = false;
        m_NodeIndex.Clear();
        m_PersistenceLocked = false;
        m_LockReason = string.Empty;
    }

    private void ClearTransientState()
    {
        m_IntentRecord.Clear();
        m_DirectionIntent.Clear();
        m_RestoreCursor = 0;
        m_RestoreVisitsRemaining = 0;
        m_PrefabEntitiesByName.Clear();
        m_NameMapStale = true;
        m_NodeIndexBuilt = false;
        m_NodeIndex.Clear();
        m_ForeignPayload = null;
        DataTrusted = true;
    }

    // The game's component serializer already supplies the outer block. Legacy V1/V2
    // start with their version; schema 3/4 start with a byte count, then the RFLT body.
    // Dispatch consumes the first integer exactly once, without reader copies or rewind.
    private const int MaxPayloadBytes = RestrictionByteSink.MaxPayloadBytes;

    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
    {
        byte[] bytes;
        if (m_PersistenceLocked)
        {
            if (m_ForeignPayload == null)
                throw new InvalidOperationException("RouteFilter refuses to overwrite unreadable legacy data. Reset explicitly before saving.");
            bytes = m_ForeignPayload;
        }
        else
        {
            var sink = new RestrictionByteSink();
            RouteFilterSaveCodec.Encode(CaptureCurrentConfiguration(), sink);
            bytes = sink.ToArray();
        }
        RestrictionNativeIO.WriteBody(writer, bytes);
    }

    public void Deserialize<TReader>(TReader reader) where TReader : IReader
    {
        SetDefaults(reader.context);
        reader.Read(out int first);
        if (first == 1 || first == 2)
        {
            ReadLegacyGroup(reader, 0);
            ReadLegacyGroup(reader, 1);
            return;
        }
        if (first < 8 || first > MaxPayloadBytes)
        {
            LockPayload("Invalid RouteFilter payload length", null);
            throw new InvalidOperationException(m_LockReason);
        }
        var bytes = RestrictionNativeIO.ReadBody(reader, first);
        var result = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
        if (!result.IsApplicable || result.Status == SaveDecodeStatus.PartiallyRecovered)
        {
            // Keep the entire envelope, including magic/schema/flags, for exact re-emission.
            LockPayload(result.Detail, bytes);
            return;
        }
        QueueRestrictions(result.Data);
    }

    private void LockPayload(string reason, byte[] original)
    {
        m_IntentRecord.Clear();
        m_DirectionIntent.Clear();
        m_PersistenceLocked = true;
        DataTrusted = false;
        m_LockReason = reason;
        m_ForeignPayload = original;
        Mod.Log.Error("[RouteFilter.Persistence] Configuration locked: " + reason);
    }

    private void ReadLegacyGroup<TReader>(TReader reader, byte kind) where TReader : IReader
    {
        reader.Read(out int count);
        if (count < 0 || count > RouteFilterSaveData.MaxRestrictionCount)
        {
            LockPayload("Invalid legacy target count", null);
            throw new InvalidOperationException(m_LockReason);
        }
        for (var i = 0; i < count; i++)
        {
            // IReader.Read(Entity) resolves ONE serialized table index through the game's
            // entity remapping table. It is not a raw Index/Version pair.
            reader.Read(out Entity target);
            reader.Read(out int assetCount);
            if (assetCount < 0 || assetCount > RouteFilterSaveData.MaxPrefabsPerRestriction)
            {
                LockPayload("Invalid legacy asset count", null);
                throw new InvalidOperationException(m_LockReason);
            }
            var pending = new IntentRecord { IsLegacy = true, LegacyTarget = target, LegacyKind = kind };
            for (var j = 0; j < assetCount; j++)
            {
                string name;
                try { name = RestrictionNativeIO.ReadName(reader); }
                catch (InvalidOperationException error) { LockPayload(error.Message, null); throw; }
                if (name.Length != 0) pending.AssetNames.Add(name);
            }
            if (pending.AssetNames.Count != 0 && target != Entity.Null)
            { m_IntentRecord.Add(pending); m_RestoreVisitsRemaining += RestorePasses; }
            else if (pending.AssetNames.Count != 0)
                Mod.Log.Warn("[RouteFilter.Persistence] Missing legacy target: restriction skipped, no target guessed.");
        }
    }

    private void QueueRestrictions(RouteFilterSaveData data)
    {
        if (data.DirectionFallbackCount != 0)
            Mod.Log.Warn($"[RouteFilter.Directions] {data.DirectionFallbackCount} invalid direction records restored as ALL entries; forbidden prefab intent retained");
        if (data == null || data.Restrictions.Count == 0) return;
        var unique = new Dictionary<RestrictionTargetIdentity, IntentRecord>();
        for (var i = 0; i < data.Restrictions.Count; i++)
        {
            var restriction = data.Restrictions[i];
            if (!unique.TryGetValue(restriction.Target, out var pending))
            {
                pending = new IntentRecord { Identity = restriction.Target };
                pending.EnabledEntries = restriction.EnabledEntries;
                unique.Add(restriction.Target, pending);
            }
            else if (!SameEntries(pending.EnabledEntries, restriction.EnabledEntries))
            {
                pending.EnabledEntries = null;
                Mod.Log.Warn("[RouteFilter.Directions] conflicting duplicate target direction sets; keeping all-entry compatibility");
            }
            var indices = restriction.PrefabIndices;
            if (indices != null)
                for (var j = 0; j < indices.Length; j++)
                {
                    var index = indices[j];
                    if (index < 0 || index >= data.PrefabNames.Count) continue;
                    var name = data.PrefabNames[index];
                    if (!string.IsNullOrEmpty(name)) pending.AssetNames.Add(name);
                }
        }
        foreach (var pending in unique.Values)
        {
            if (pending.AssetNames.Count == 0) continue;
            m_IntentRecord.Add(pending);
            m_RestoreVisitsRemaining += RestorePasses;
        }
    }

    private RouteFilterSaveData CaptureCurrentConfiguration()
    {
        var data = RouteFilterSaveData.CreateEmpty();
        var nameIndices = new Dictionary<string, int>(64);

        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        for (var i = 0; i < nodes.Length; i++)
            CaptureTarget(data, nameIndices, nodes[i], 0);

        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        for (var i = 0; i < segments.Length; i++)
            CaptureTarget(data, nameIndices, segments[i], 1);
        CapturePending(data, nameIndices);
        return data;
    }

    private void CaptureTarget(
        RouteFilterSaveData data,
        Dictionary<string, int> nameIndices,
        Entity target,
        byte kind)
    {
        if (target == Entity.Null || !EntityManager.Exists(target)) return;
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets) ||
            assets.Length == 0) return;

        if (!TryDescribeTarget(target, kind, out var identity)) return;

        var indices = new List<int>(assets.Length);
        for (var i = 0; i < assets.Length; i++)
        {
            var prefab = assets[i].m_Prefab;
            if (prefab == Entity.Null) continue;
            var name = m_PrefabSystem.GetPrefabName(prefab);
            if (string.IsNullOrEmpty(name)) continue;
            if (!nameIndices.TryGetValue(name, out var index))
            {
                index = data.PrefabNames.Count;
                data.PrefabNames.Add(name);
                nameIndices.Add(name, index);
            }
            indices.Add(index);
        }
        if (indices.Count == 0) return;

        data.Restrictions.Add(new PersistentRestriction
        {
            Target = identity,
            PrefabIndices = indices.ToArray(),
            EnabledEntries = GetDirectionIntent(target)
        });
    }

    internal bool TryDescribeTarget(Entity target, byte kind, out RestrictionTargetIdentity identity)
    {
        identity = default;
        if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target) ||
            EntityManager.HasComponent<Temp>(target)) return false;
        if (kind == 0)
        {
            if (!EntityManager.TryGetComponent(target, out Node node)) return false;
            identity.Kind = 0;
            identity.Anchor = RestrictionAnchor.Quantize(node.m_Position.x, node.m_Position.y, node.m_Position.z);
            return true;
        }

        if (!EntityManager.TryGetComponent(target, out Edge edge)) return false;
        if (!EntityManager.TryGetComponent(target, out Curve curve)) return false;
        if (!EntityManager.HasComponent<Node>(edge.m_Start) || !EntityManager.HasComponent<Node>(edge.m_End)) return false;
        var start = EntityManager.GetComponentData<Node>(edge.m_Start);
        var end = EntityManager.GetComponentData<Node>(edge.m_End);
        identity.Kind = 1;
        identity.Anchor = RestrictionAnchor.Quantize(start.m_Position.x, start.m_Position.y, start.m_Position.z);
        identity.EndAnchor = RestrictionAnchor.Quantize(end.m_Position.x, end.m_Position.y, end.m_Position.z);
        identity.LengthCentimetres = (int)System.Math.Round(curve.m_Length * 100f, System.MidpointRounding.AwayFromZero);
        return true;
    }

    // Load/content-change work only: a bounded number of passes, no per-frame allocations/logs.
    // Unavailable names/targets remain player intent and are re-emitted on save.
    private void TryRestorePendingRestrictions()
    {
        if (m_NameMapStale) BuildPrefabNameMap();
        if (!m_NodeIndexBuilt) BuildNodeIndex();
        var tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        var processed = 0;
        while (m_IntentRecord.Count != 0 && processed < 64 && m_RestoreVisitsRemaining > 0)
        {
            if (m_RestoreCursor >= m_IntentRecord.Count) m_RestoreCursor = 0;
            var i = m_RestoreCursor++;
            var record = m_IntentRecord[i];
            m_RestoreVisitsRemaining--;
            processed++;
            record.Attempts++;
            if (record.IsLegacy)
            {
                if (!TryDescribeTarget(record.LegacyTarget, record.LegacyKind, out record.Identity)) continue;
                record.IsLegacy = false;
            }
            if (!TryResolveTarget(record.Identity, out var target)) continue;
            record.ResolvedTarget = target;
            m_ResolvedAssets.Clear();
            var changed = false;
            foreach (var name in record.AssetNames)
            {
                if (!m_PrefabEntitiesByName.TryGetValue(name, out var prefab) || prefab == Entity.Null) continue;
                m_ResolvedAssets.Add(prefab);
                changed |= record.ResolvedNames.Add(name);
            }
            if (changed) tool.RestoreRestriction(target, record.Identity.Kind == 0, m_ResolvedAssets, record.EnabledEntries);
            // Retain the complete player intent even after successful resolution. The ECS
            // buffer cannot preserve a name if its asset becomes unavailable later.
        }
    }

    public void RememberIntent(Entity target, byte kind, RestrictionEntryIdentity[] entries = null)
    {
        if (!ConfigurationEditable || !TryDescribeTarget(target, kind, out var identity)) return;
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets)) return;
        ForgetPending(target);
        var intent = new IntentRecord { Identity = identity, ResolvedTarget = target, EnabledEntries = entries };
        SetDirectionIntent(target, entries);
        foreach (var asset in assets)
        {
            var name = m_PrefabSystem.GetPrefabName(asset.m_Prefab);
            if (!string.IsNullOrEmpty(name)) { intent.AssetNames.Add(name); intent.ResolvedNames.Add(name); }
        }
        if (intent.AssetNames.Count != 0) m_IntentRecord.Add(intent);
    }

    internal RestrictionEntryIdentity[] GetDirectionIntent(Entity target)
        => m_DirectionIntent.TryGetValue(target, out var entries) ? entries : null;

    internal void SetDirectionIntent(Entity target, RestrictionEntryIdentity[] entries)
    {
        if (entries == null) m_DirectionIntent.Remove(target);
        else m_DirectionIntent[target] = (RestrictionEntryIdentity[])entries.Clone();
    }

    private static bool SameEntries(RestrictionEntryIdentity[] a, RestrictionEntryIdentity[] b)
    {
        if (a == null || b == null) return a == b;
        return new HashSet<RestrictionEntryIdentity>(a).SetEquals(b);
    }

    public void ForgetPending(Entity target)
    {
        m_DirectionIntent.Remove(target);
        for (var i = m_IntentRecord.Count - 1; i >= 0; i--)
        {
            var record = m_IntentRecord[i];
            if (record.ResolvedTarget == target || (record.IsLegacy ? record.LegacyTarget == target :
                TryDescribeTarget(target, record.Identity.Kind, out var identity) && identity.Matches(record.Identity)))
                m_IntentRecord.RemoveAt(i);
        }
    }

    private void CapturePending(RouteFilterSaveData data, Dictionary<string, int> names)
    {
        // A target known to have been deleted is different from a target missing during load.
        // Retire known deletions; unresolved identities remain quarantined player intent.
        for (var i = m_IntentRecord.Count - 1; i >= 0; i--)
        {
            var target = m_IntentRecord[i].ResolvedTarget;
            if (target == Entity.Null || (EntityManager.Exists(target) && !EntityManager.HasComponent<Deleted>(target) &&
                !EntityManager.HasComponent<Temp>(target))) continue;
            m_DirectionIntent.Remove(target); m_IntentRecord.RemoveAt(i);
        }
        var recordIndices = new Dictionary<RestrictionTargetIdentity, int>();
        for (var i = 0; i < data.Restrictions.Count; i++) recordIndices[data.Restrictions[i].Target] = i;
        foreach (var pending in m_IntentRecord)
        {
            if (pending.ResolvedTarget != Entity.Null &&
                TryDescribeTarget(pending.ResolvedTarget, pending.Identity.Kind, out var currentIdentity))
                pending.Identity = currentIdentity;
            if (pending.IsLegacy)
            {
                if (!TryDescribeTarget(pending.LegacyTarget, pending.LegacyKind, out pending.Identity))
                    throw new InvalidOperationException("RouteFilter cannot safely save an unresolved legacy target; wait for load or explicitly Reset.");
                pending.IsLegacy = false;
            }
            var indices = new HashSet<int>();
            var recordIndex = recordIndices.TryGetValue(pending.Identity, out var found) ? found : -1;
            if (recordIndex >= 0)
                foreach (var index in data.Restrictions[recordIndex].PrefabIndices) indices.Add(index);
            foreach (var name in pending.AssetNames)
            {
                if (!names.TryGetValue(name, out var index))
                {
                    index = data.PrefabNames.Count;
                    data.PrefabNames.Add(name);
                    names.Add(name, index);
                }
                indices.Add(index);
            }
            var array = new int[indices.Count];
            indices.CopyTo(array);
            var record = new PersistentRestriction { Target = pending.Identity, PrefabIndices = array, EnabledEntries = pending.EnabledEntries };
            if (recordIndex >= 0) data.Restrictions[recordIndex] = record;
            else { recordIndices[pending.Identity] = data.Restrictions.Count; data.Restrictions.Add(record); }
        }
    }

    /// <summary>
    /// Exact identity resolution. A target that cannot be matched uniquely is reported as
    /// unresolved; RouteFilter never falls back to the nearest node or the nearest segment,
    /// because a restriction on the wrong intersection is worse than no restriction.
    /// </summary>
    private bool TryResolveTarget(in RestrictionTargetIdentity identity, out Entity target)
    {
        target = Entity.Null;
        if (m_NodeIndex.Count == 0) return false;

        if (identity.Kind == 0)
            return TryFindNode(identity.Anchor, out target);

        if (!TryFindNode(identity.Anchor, out var start) || !TryFindNode(identity.EndAnchor, out var end))
            return false;

        if (!EntityManager.TryGetBuffer(start, true, out DynamicBuffer<ConnectedEdge> edges)) return false;
        var matches = 0;
        var match = Entity.Null;
        for (var i = 0; i < edges.Length; i++)
        {
            var edge = edges[i].m_Edge;
            if (edge == Entity.Null || !EntityManager.HasComponent<Edge>(edge)) continue;
            if (!EntityManager.TryGetComponent(edge, out Edge data)) continue;
            var matchesOrientation =
                (data.m_Start == start && data.m_End == end) || (data.m_Start == end && data.m_End == start);
            if (!matchesOrientation) continue;
            if (!EntityManager.TryGetComponent(edge, out Curve curve)) continue;
            if ((int)System.Math.Round(curve.m_Length * 100f, System.MidpointRounding.AwayFromZero) != identity.LengthCentimetres) continue;
            matches++;
            match = edge;
        }
        if (matches != 1) return false;
        target = match;
        return true;
    }

    private bool TryFindNode(RestrictionAnchor anchor, out Entity node)
    {
        node = Entity.Null;
        return m_NodeIndex.TryGetValue(anchor, out node) && node != Entity.Null;
    }

    /// <summary>
    /// Builds the anchor -> node lookup used for exact identity resolution. Anchors that appear
    /// more than once are stored as <see cref="Entity.Null"/> so an ambiguous position can never
    /// resolve to an arbitrary one of its candidates.
    /// </summary>
    private void BuildNodeIndex()
    {
        m_NodeIndex.Clear();
        using var nodes = m_NodeQuery.ToEntityArray(Allocator.Temp);
        for (var i = 0; i < nodes.Length; i++)
        {
            if (!EntityManager.TryGetComponent(nodes[i], out Node data)) continue;
            var anchor = RestrictionAnchor.Quantize(data.m_Position.x, data.m_Position.y, data.m_Position.z);
            if (m_NodeIndex.TryGetValue(anchor, out var existing))
            {
                if (existing == Entity.Null) continue;
                m_NodeIndex[anchor] = Entity.Null;
                continue;
            }
            m_NodeIndex[anchor] = nodes[i];
        }
        m_NodeIndexBuilt = true;
    }

    private void BuildPrefabNameMap()
    {
        m_PrefabEntitiesByName.Clear();
        using var prefabEntities = m_VehiclePrefabQuery.ToEntityArray(Allocator.Temp);
        for (var i = 0; i < prefabEntities.Length; i++)
        {
            var name = m_PrefabSystem.GetPrefabName(prefabEntities[i]);
            if (string.IsNullOrEmpty(name)) continue;
            // Ambiguous names never bind to an arbitrary asset.
            if (m_PrefabEntitiesByName.ContainsKey(name)) m_PrefabEntitiesByName[name] = Entity.Null;
            else m_PrefabEntitiesByName.Add(name, prefabEntities[i]);
        }
        m_NameMapStale = false;
        Mod.Log.Info($"[RouteFilter.Persistence] prefab name map rebuilt with {m_PrefabEntitiesByName.Count} entries");
    }
}
