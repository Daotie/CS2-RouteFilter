using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
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
/// Three properties are structural rather than best-effort:
/// <list type="number">
/// <item>The payload is framed with its own exact byte length, so the game's
/// <c>ComponentSystemSerializer</c> size check always matches even when the body is corrupt.</item>
/// <item>A payload written by a newer schema is copied verbatim and re-emitted verbatim. It is
/// never reinterpreted and never overwritten.</item>
/// <item>A payload whose header cannot be trusted disables enforcement and locks persistence, so
/// a half-parsed save is never half-applied and never silently replaced.</item>
/// </list>
/// </summary>
public sealed partial class RestrictionPersistenceSystem : GameSystemBase, IDefaultSerializable
{
    private const int MaxRestoreAttempts = 600;
    private const int MaxUnresolvedPrefabRetries = 600;

    private sealed class PendingRestore
    {
        public RestrictionTargetIdentity Identity;
        public readonly List<string> AssetNames = new();
        public readonly HashSet<string> ResolvedNames = new();
        public int Attempts;
    }

    private sealed class WriterSink : IRestrictionSaveSink
    {
        private readonly IWriter m_Writer;
        internal WriterSink(IWriter writer) => m_Writer = writer;

        public void WriteUInt(uint value) => m_Writer.Write(value);
        public void WriteUShort(ushort value) => m_Writer.Write(value);
        public void WriteInt(int value) => m_Writer.Write(value);
        public void WriteFloat(float value) => m_Writer.Write(value);
        public void WriteString(string value) => m_Writer.Write(value ?? string.Empty);

        public void WriteBytes(byte[] value)
        {
            if (value == null || value.Length == 0) return;
            using var native = new NativeArray<byte>(value, Allocator.Temp);
            m_Writer.Write(native);
        }
    }

    private sealed class ReaderSource : IRestrictionSaveSource
    {
        private readonly IReader m_Reader;
        private long m_Consumed;
        private long m_Total;

        internal ReaderSource(IReader reader, long total)
        {
            m_Reader = reader;
            m_Total = total;
        }

        public long Remaining => m_Total < 0 ? long.MaxValue : m_Total - m_Consumed;

        public bool ReadUInt(out uint value)
        {
            if (Remaining < 4) { value = 0; return false; }
            m_Reader.Read(out value);
            m_Consumed += 4;
            return true;
        }

        public bool ReadUShort(out ushort value)
        {
            if (Remaining < 2) { value = 0; return false; }
            m_Reader.Read(out value);
            m_Consumed += 2;
            return true;
        }

        public bool ReadInt(out int value)
        {
            if (Remaining < 4) { value = 0; return false; }
            m_Reader.Read(out value);
            m_Consumed += 4;
            return true;
        }

        public bool ReadString(out string value)
        {
            value = null;
            if (Remaining < 4) return false;
            m_Reader.Read(out int length);
            m_Consumed += 4;
            if (length < 0 || length > RouteFilterSaveData.MaxPrefabNameLength || Remaining < (long)length * 2)
                return false;
            m_Reader.Read(out value);
            m_Consumed += (long)length * 2;
            return value != null;
        }

        public bool SkipToEnd()
        {
            if (m_Total < 0) return false;
            var remaining = (int)Remaining;
            if (remaining <= 0) return true;
            m_Reader.Skip(remaining);
            m_Consumed = m_Total;
            return true;
        }

        public bool ReadRemainingBytes(out byte[] value)
        {
            value = null;
            if (m_Total < 0) return false;
            var remaining = Remaining;
            if (remaining <= 0 || remaining > int.MaxValue) return false;
            using var native = new NativeArray<byte>((int)remaining, Allocator.Temp);
            m_Reader.Read(native);
            value = native.ToArray();
            m_Consumed = m_Total;
            return true;
        }
    }

    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private EntityQuery m_VehiclePrefabQuery;
    private EntityQuery m_NodeQuery;
    private PrefabSystem m_PrefabSystem = null!;

    private readonly List<PendingRestore> m_PendingRestore = new();
    private readonly Dictionary<string, Entity> m_PrefabEntitiesByName = new();
    private readonly Dictionary<RestrictionAnchor, Entity> m_NodeIndex = new();
    private readonly List<Entity> m_ResolvedAssets = new();

    private bool m_NameMapStale = true;
    private bool m_NodeIndexBuilt;
    private byte[] m_ForeignPayload;
    private ushort m_ForeignSchema;
    private bool m_PersistenceLocked;
    private string m_LockReason = string.Empty;

    /// <summary>True while the payload must be re-emitted byte for byte instead of re-encoded.</summary>
    public bool PersistenceLocked => m_PersistenceLocked;

    /// <summary>True when the stored payload cannot be trusted, so enforcement must stay off.</summary>
    public bool DataTrusted { get; private set; } = true;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_PrefabSystem.onContentAvailabilityChanged += OnContentAvailabilityChanged;
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_VehiclePrefabQuery = GetEntityQuery(
            ComponentType.ReadOnly<VehicleData>(),
            ComponentType.ReadOnly<PrefabData>());
        m_NodeQuery = GetEntityQuery(ComponentType.ReadOnly<Node>());
    }

    protected override void OnDestroy()
    {
        m_PrefabSystem.onContentAvailabilityChanged -= OnContentAvailabilityChanged;
        base.OnDestroy();
    }

    private void OnContentAvailabilityChanged()
    {
        m_NameMapStale = true;
        Mod.Log.Info("[RouteFilter.Persistence] Content availability changed; prefab name map will be rebuilt");
    }

    protected override void OnUpdate()
    {
        if (m_PendingRestore.Count == 0) return;
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
    }

    private void ClearTransientState()
    {
        m_PendingRestore.Clear();
        m_PrefabEntitiesByName.Clear();
        m_NameMapStale = true;
        m_NodeIndexBuilt = false;
        m_NodeIndex.Clear();
        m_ForeignPayload = null;
        m_ForeignSchema = 0;
        DataTrusted = true;
    }

    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
    {
        // Always frame our own payload. The game asserts that exactly `payloadLength` bytes were
        // consumed on load, and this is what makes a corrupt body a skipped restriction instead
        // of a failed city load.
        var block = writer.Begin();
        if (m_PersistenceLocked && m_ForeignPayload != null)
        {
            // Verbatim re-emission. No interpretation, no re-encoding, no data loss.
            var sink = new WriterSink(writer);
            sink.WriteBytes(m_ForeignPayload);
            writer.End(block);
            Mod.Log.Warn($"[RouteFilter.Persistence] Payload preserved verbatim: {m_LockReason}");
            return;
        }

        var data = CaptureCurrentConfiguration();
        RouteFilterSaveCodec.Encode(data, new WriterSink(writer));
        writer.End(block);
        Mod.Log.Info($"[RouteFilter.Persistence] serialize schema={RouteFilterSaveData.SchemaVersion} " +
                     $"targets={data.Restrictions.Count} prefabNames={data.PrefabNames.Count}");
    }

    public void Deserialize<TReader>(TReader reader) where TReader : IReader
    {
        ClearTransientState();
        m_PersistenceLocked = false;
        m_LockReason = string.Empty;
        DataTrusted = true;

        // Our own length prefix. Present for every payload RouteFilter has written since schema 3.
        var block = reader.Begin(out var payloadSize);

        if (!DecodeCurrentSchema(reader, payloadSize))
        {
            // Not a schema-3 payload. The legacy layout has no length prefix, so the reader is
            // recreated with an unknown total and exactness relies on the legacy layout itself.
            reader.End(block);
            DecodeLegacyPayload(reader);
        }

        Mod.Log.Info($"[RouteFilter.Persistence] deserialize queued={m_PendingRestore.Count} " +
                     $"locked={m_PersistenceLocked} trusted={DataTrusted}");
    }

    private bool DecodeCurrentSchema<TReader>(TReader reader, int payloadSize) where TReader : IReader
    {
        var probe = reader;
        var source = new ReaderSource(probe, payloadSize);
        var result = RouteFilterSaveCodec.Decode(source);
        if (result.Status == SaveDecodeStatus.NotRouteFilterData) return false;

        switch (result.Status)
        {
            case SaveDecodeStatus.FutureSchema:
                m_ForeignPayload = result.ForeignPayload;
                m_ForeignSchema = result.ForeignSchema;
                m_PersistenceLocked = true;
                m_LockReason = result.Detail;
                DataTrusted = false;
                Mod.Log.Warn($"[RouteFilter.Persistence] {result.Detail}. " +
                             "RouteFilter enforcement stays disabled for this save and the payload is preserved unchanged.");
                return true;

            case SaveDecodeStatus.Corrupt:
                // The header parsed but the body is not usable. Enforce nothing, rewrite nothing:
                // half-applying a damaged payload and then overwriting it destroys the only copy.
                m_PersistenceLocked = true;
                m_LockReason = result.Detail;
                DataTrusted = false;
                Mod.Log.Error($"[RouteFilter.Persistence] corrupt payload ({result.Detail}). " +
                              "Enforcement disabled and payload left untouched; use Reset to clear it deliberately.");
                return true;

            case SaveDecodeStatus.PartiallyRecovered:
                Mod.Log.Warn($"[RouteFilter.Persistence] {result.Detail}");
                QueueRestrictions(result.Data);
                return true;

            default:
                QueueRestrictions(result.Data);
                return true;
        }
    }

    private void DecodeLegacyPayload<TReader>(TReader reader) where TReader : IReader
    {
        // Nothing in this stream is length-framed, so Remaining is unknown and SkipToEnd is a
        // no-op. A malformed legacy payload therefore still has to be read exactly; the clamps in
        // LegacyRouteFilterSaveCodec are what keep that true for every count that can occur.
        var source = new ReaderSource(reader, -1);
        var legacy = LegacyRouteFilterSaveCodec.Decode(source);
        if (legacy.Status == LegacyDecodeStatus.NotLegacyPayload)
        {
            Mod.Log.Warn($"[RouteFilter.Persistence] unrecognised payload ({legacy.Detail}); " +
                         "RouteFilter enforcement stays disabled until the configuration is rebuilt.");
            m_PersistenceLocked = true;
            m_LockReason = legacy.Detail;
            DataTrusted = false;
            return;
        }
        if (legacy.Status == LegacyDecodeStatus.Corrupt)
        {
            Mod.Log.Error($"[RouteFilter.Persistence] legacy payload corrupt ({legacy.Detail}); nothing applied.");
            m_PersistenceLocked = true;
            m_LockReason = legacy.Detail;
            DataTrusted = false;
            return;
        }

        var tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        var resolved = 0;
        var skipped = 0;
        foreach (var record in legacy.Data.Records)
        {
            var entity = new Entity { Index = record.EntityIndex, Version = record.EntityVersion };
            if (!EntityManager.Exists(entity))
            {
                skipped++;
                continue;
            }
            var isNode = EntityManager.HasComponent<Node>(entity);
            var isSegment = EntityManager.HasComponent<Edge>(entity);
            if ((record.Kind == 0 && !isNode) || (record.Kind == 1 && !isSegment) ||
                record.PrefabNames.Count == 0)
            {
                skipped++;
                continue;
            }
            var assets = new List<Entity>(record.PrefabNames.Count);
            foreach (var name in record.PrefabNames)
                if (m_PrefabEntitiesByName.Count != 0 && m_PrefabEntitiesByName.TryGetValue(name, out var prefab))
                    assets.Add(prefab);
            tool.RestoreRestriction(entity, record.Kind == 0, assets);
            resolved++;
        }
        Mod.Log.Info($"[RouteFilter.Persistence] migrated {legacy.Detail}; applied={resolved} skipped={skipped}");
    }

    private void QueueRestrictions(RouteFilterSaveData data)
    {
        if (data == null || data.Restrictions.Count == 0) return;
        for (var i = 0; i < data.Restrictions.Count; i++)
        {
            var restriction = data.Restrictions[i];
            var pending = new PendingRestore { Identity = restriction.Target };
            var indices = restriction.PrefabIndices;
            if (indices != null)
                for (var j = 0; j < indices.Length; j++)
                {
                    var index = indices[j];
                    if (index < 0 || index >= data.PrefabNames.Count) continue;
                    var name = data.PrefabNames[index];
                    if (!string.IsNullOrEmpty(name)) pending.AssetNames.Add(name);
                }
            if (pending.AssetNames.Count == 0) continue;
            m_PendingRestore.Add(pending);
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
            PrefabIndices = indices.ToArray()
        });
    }

    private bool TryDescribeTarget(Entity target, byte kind, out RestrictionTargetIdentity identity)
    {
        identity = default;
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

    private void TryRestorePendingRestrictions()
    {
        var tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();

        if (m_NameMapStale || m_PrefabEntitiesByName.Count == 0) BuildPrefabNameMap();
        if (!m_NodeIndexBuilt) BuildNodeIndex();

        var applied = 0;
        var waitingTarget = 0;
        var unresolvedPrefab = 0;
        var droppedTarget = 0;
        var remaining = new List<PendingRestore>(m_PendingRestore.Count);

        for (var i = 0; i < m_PendingRestore.Count; i++)
        {
            var record = m_PendingRestore[i];
            record.Attempts++;

            if (!TryResolveTarget(record.Identity, out var target))
            {
                if (record.Attempts < MaxRestoreAttempts) { waitingTarget++; remaining.Add(record); }
                else droppedTarget++;
                continue;
            }

            var resolvedAny = false;
            var allResolved = true;
            m_ResolvedAssets.Clear();
            for (var j = 0; j < record.AssetNames.Count; j++)
            {
                var name = record.AssetNames[j];
                if (!m_PrefabEntitiesByName.TryGetValue(name, out var prefab))
                {
                    allResolved = false;
                    continue;
                }
                resolvedAny = true;
                m_ResolvedAssets.Add(prefab);
            }

            if (!resolvedAny)
            {
                // Every forbidden prefab for this target is unavailable, most often because an
                // asset pack is not installed. Skipping is the only honest option: a restriction
                // with no items is not a restriction, and guessing a similarly named prefab would
                // ban the wrong vehicles.
                if (record.Attempts < MaxUnresolvedPrefabRetries) unresolvedPrefab += record.AssetNames.Count;
                continue;
            }

            if (!allResolved && record.Attempts < MaxUnresolvedPrefabRetries)
            {
                // Partially resolvable. Applying now would overwrite the buffer and lose the
                // unresolved names, so the record waits until it is complete.
                unresolvedPrefab += record.AssetNames.Count;
                remaining.Add(record);
                continue;
            }

            // A missing prefab costs that item only; the remaining items still apply.
            tool.RestoreRestriction(target, record.Identity.Kind == 0, m_ResolvedAssets);
            applied++;
            if (!allResolved)
                Mod.Log.Warn($"[RouteFilter.Persistence] target {target.Index}:{target.Version}: " +
                             $"{record.AssetNames.Count - m_ResolvedAssets.Count} forbidden prefab(s) could not be " +
                             "resolved and were not applied to this target");
        }

        m_PendingRestore.Clear();
        m_PendingRestore.AddRange(remaining);
        if (applied != 0 || droppedTarget != 0 || m_PendingRestore.Count != 0)
            Mod.Log.Info($"[RouteFilter.Persistence] restore applied={applied} waitingTargets={waitingTarget} " +
                         $"unresolvedPrefabs={unresolvedPrefab} droppedTargets={droppedTarget} pending={m_PendingRestore.Count}");
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
            if ((int)math.round(curve.m_Length * 100f) != identity.LengthCentimetres) continue;
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
            // First match wins, matching the behaviour the legacy restore path relied on.
            if (!m_PrefabEntitiesByName.ContainsKey(name)) m_PrefabEntitiesByName.Add(name, prefabEntities[i]);
        }
        m_NameMapStale = false;
        Mod.Log.Info($"[RouteFilter.Persistence] prefab name map rebuilt with {m_PrefabEntitiesByName.Count} entries");
    }
}
