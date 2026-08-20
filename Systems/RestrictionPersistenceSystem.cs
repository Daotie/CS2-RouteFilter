using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Net;
using Game.Prefabs;
using RouteFilter.Components;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Owns the authoritative, versioned restriction payload that travels inside the save file.
/// Forbidden vehicle assets are stored as stable prefab names (not raw entity references,
/// which do not round-trip reliably for prefab entities) and re-resolved after loading,
/// so the saved asset lists survive save/reload.
/// </summary>
public sealed partial class RestrictionPersistenceSystem : GameSystemBase, IDefaultSerializable
{
    private const int SaveVersion = 2;
    private const int MaxRecordCount = 100000;

    private sealed class RestrictionRecord
    {
        public Entity Target;
        public bool IsNode;
        public readonly List<string> AssetNames = new();
    }

    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private EntityQuery m_VehiclePrefabQuery;
    private PrefabSystem m_PrefabSystem = null!;
    private readonly List<RestrictionRecord> m_PendingRestore = new();
    private readonly Dictionary<string, Entity> m_PrefabEntitiesByName = new();
    private readonly List<Entity> m_ResolvedAssets = new();
    private bool m_NameMapBuilt;
    private int m_RestoreAttempts;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Game.Net.Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_VehiclePrefabQuery = GetEntityQuery(
            ComponentType.ReadOnly<VehicleData>(),
            ComponentType.ReadOnly<PrefabData>());
    }

    protected override void OnUpdate()
    {
        if (m_PendingRestore.Count == 0) return;
        TryRestorePendingRestrictions();
    }

    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
    {
        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);

        writer.Write(SaveVersion);
        writer.Write(nodes.Length);
        foreach (var target in nodes) WriteRestrictedTarget(writer, target);
        writer.Write(segments.Length);
        foreach (var target in segments) WriteRestrictedTarget(writer, target);

        Mod.Log.Info($"Persistence serialize: {nodes.Length} restricted nodes, {segments.Length} restricted segments");
    }

    public void Deserialize<TReader>(TReader reader) where TReader : IReader
    {
        m_PendingRestore.Clear();
        m_NameMapBuilt = false;
        m_RestoreAttempts = 0;
        reader.Read(out int version);
        if (version != SaveVersion)
        {
            Mod.Log.Warn($"Persistence deserialize: unsupported save data version {version}; keeping per-entity data only");
            return;
        }

        reader.Read(out int nodeCount);
        var nodes = ReadRestrictedTargets(reader, nodeCount, true, out var nodeTruncated);
        reader.Read(out int segmentCount);
        var segments = ReadRestrictedTargets(reader, segmentCount, false, out var segmentTruncated);

        m_PendingRestore.AddRange(nodes);
        m_PendingRestore.AddRange(segments);
        Mod.Log.Info($"Persistence deserialize: queued restore of {m_PendingRestore.Count} targets " +
            $"({nodes.Count} nodes, {segments.Count} segments; truncation: node={nodeTruncated}, segment={segmentTruncated})");
    }

    public void SetDefaults(Context context)
    {
        m_PendingRestore.Clear();
        m_NameMapBuilt = false;
        m_RestoreAttempts = 0;
    }

    private void WriteRestrictedTarget<TWriter>(TWriter writer, Entity target) where TWriter : IWriter
    {
        writer.Write(target);
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets))
        {
            writer.Write(0);
            return;
        }
        writer.Write(assets.Length);
        foreach (var asset in assets)
            writer.Write(m_PrefabSystem.GetPrefabName(asset.m_Prefab));
    }

    private List<RestrictionRecord> ReadRestrictedTargets<TReader>(TReader reader, int count, bool isNode, out bool truncated)
        where TReader : IReader
    {
        var records = new List<RestrictionRecord>();
        truncated = false;
        if (count < 0 || count > MaxRecordCount)
        {
            Mod.Log.Warn($"Persistence deserialize: suspicious target count {count}; clamping to {MaxRecordCount}");
            count = MaxRecordCount;
            truncated = true;
        }

        for (var i = 0; i < count; i++)
        {
            var record = new RestrictionRecord { IsNode = isNode };
            reader.Read(out record.Target);
            reader.Read(out int assetCount);
            if (assetCount < 0 || assetCount > MaxRecordCount)
            {
                Mod.Log.Warn($"Persistence deserialize: suspicious asset count {assetCount} for target {record.Target.Index}:{record.Target.Version}; clamping");
                assetCount = MaxRecordCount;
                truncated = true;
            }
            for (var j = 0; j < assetCount; j++)
            {
                reader.Read(out string name);
                record.AssetNames.Add(name ?? string.Empty);
            }
            records.Add(record);
        }
        return records;
    }

    private void TryRestorePendingRestrictions()
    {
        var tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        if (!m_NameMapBuilt)
        {
            BuildPrefabNameMap();
            m_NameMapBuilt = true;
        }
        m_RestoreAttempts++;

        var restored = 0;
        var skipped = 0;
        var missingAssets = 0;
        var waiting = 0;
        var remaining = new List<RestrictionRecord>(m_PendingRestore.Count);
        foreach (var record in m_PendingRestore)
        {
            if (!EntityManager.Exists(record.Target))
            {
                waiting++;
                remaining.Add(record);
                continue;
            }

            var isNode = EntityManager.HasComponent<Node>(record.Target);
            var isSegment = EntityManager.HasComponent<Game.Net.Edge>(record.Target);
            if (!isNode && !isSegment)
            {
                skipped++;
                continue;
            }
            if (record.IsNode != isNode)
            {
                skipped++;
                continue;
            }

            m_ResolvedAssets.Clear();
            foreach (var name in record.AssetNames)
            {
                if (name.Length == 0 || !m_PrefabEntitiesByName.TryGetValue(name, out var prefab))
                {
                    missingAssets++;
                    continue;
                }
                m_ResolvedAssets.Add(prefab);
            }

            // If every saved asset name failed to resolve, keep whatever the game
            // restored per-entity instead of overwriting it with an empty list.
            if (record.AssetNames.Count > 0 && m_ResolvedAssets.Count == 0)
            {
                Mod.Log.Warn($"Persistence restore: could not resolve any saved asset for {record.Target.Index}:{record.Target.Version}; keeping per-entity data");
                skipped++;
                continue;
            }

            tool.RestoreRestriction(record.Target, record.IsNode, m_ResolvedAssets);
            restored++;
        }

        m_PendingRestore.Clear();
        m_PendingRestore.AddRange(remaining);
        Mod.Log.Info($"Persistence restore (attempt {m_RestoreAttempts}): applied {restored}, skipped {skipped} invalid, waiting {waiting}, unresolved assets {missingAssets}");

        if (waiting != 0 && m_RestoreAttempts >= 60)
        {
            Mod.Log.Warn($"Persistence restore: dropping {waiting} targets that never became available after {m_RestoreAttempts} attempts");
            m_PendingRestore.Clear();
        }
    }

    private void BuildPrefabNameMap()
    {
        m_PrefabEntitiesByName.Clear();
        using var prefabEntities = m_VehiclePrefabQuery.ToEntityArray(Allocator.Temp);
        foreach (var entity in prefabEntities)
        {
            var name = m_PrefabSystem.GetPrefabName(entity);
            if (string.IsNullOrEmpty(name)) continue;
            m_PrefabEntitiesByName[name] = entity;
        }
    }
}
