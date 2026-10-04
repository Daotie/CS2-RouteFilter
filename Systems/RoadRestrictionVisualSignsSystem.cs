using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using RouteFilter.Components;
using RouteFilter.Persistence;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ObjectTransform = Game.Objects.Transform;

namespace RouteFilter.Systems;

internal struct RoadRestrictionSignOwner : IComponentData
{
    internal Entity Target;
}

internal struct RoadRestrictionSignGeometryWatch : IComponentData { }

public sealed class RoadRestrictionVisualSignsSystem : GameSystemBase
{
    internal sealed class SignPrefab
    {
        internal Entity Entity;
        internal Entity Theme;
        internal string Name;
        internal string Icon;
        internal EntityArchetype Archetype;
        internal float GroundOffset;
    }

    private readonly Dictionary<string, SignPrefab> m_Prefabs = new(StringComparer.Ordinal);
    private readonly Dictionary<Entity, List<Entity>> m_Markers = new();
    private readonly Dictionary<Entity, HashSet<Entity>> m_WatchedByTarget = new();
    private readonly Dictionary<Entity, HashSet<Entity>> m_TargetsByWatch = new();
    private readonly HashSet<Entity> m_Dirty = new();
    private readonly HashSet<string> m_Warnings = new();
    private EntityQuery m_PrefabQuery;
    private EntityQuery m_OwnedQuery;
    private EntityQuery m_RestrictionQuery;
    private EntityQuery m_ChangedGeometryQuery;
    private PrefabSystem m_PrefabSystem;
    private RestrictionIndexSystem m_Index;
    private bool m_CatalogDirty = true;
    private bool m_LoadDirty;
    private bool m_Enabled;
    private bool m_SaveExcluded;
    private string m_Custom = null;
    private Entity m_Theme;
    public string Catalog { get; private set; } = string.Empty;
    public string ResolvedName { get; private set; } = string.Empty;
    public bool CustomUnavailable { get; private set; }
    public int MarkerCount { get; private set; }
    public bool ContainsPrefab(string name) => m_Prefabs.ContainsKey(name);

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<TrafficSignData>(), ComponentType.ReadOnly<StaticObjectData>(), ComponentType.ReadOnly<SpawnableObjectData>(), ComponentType.ReadOnly<PrefabData>());
        m_OwnedQuery = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<RoadRestrictionSignOwner>() }, None = new[] { ComponentType.ReadOnly<Deleted>() } });
        m_RestrictionQuery = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_ChangedGeometryQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<RoadRestrictionSignGeometryWatch>() },
            Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
        });
        m_PrefabSystem.onContentAvailabilityChanged += ContentChanged;
        GameManager.instance.onGameLoadingComplete += Loaded;
    }

    protected override void OnDestroy()
    {
        m_PrefabSystem.onContentAvailabilityChanged -= ContentChanged;
        if (GameManager.instance != null) GameManager.instance.onGameLoadingComplete -= Loaded;
        ClearOwned();
        base.OnDestroy();
    }

    private void ContentChanged() => m_CatalogDirty = true;
    private void Loaded(Purpose purpose, GameMode mode) { m_LoadDirty = true; m_CatalogDirty = true; }
    public void MarkDirty(Entity target) { if (target != Entity.Null) m_Dirty.Add(target); }

    internal void CollectGeometryChanges()
    {
        if (m_ChangedGeometryQuery.IsEmptyIgnoreFilter) return;
        using var changed = m_ChangedGeometryQuery.ToEntityArray(Allocator.Temp);
        foreach (var entity in changed)
            if (m_TargetsByWatch.TryGetValue(entity, out var targets))
                foreach (var target in targets) MarkDirty(target);
    }

    public void ResetRuntimeState()
    {
        ClearOwned(); m_Dirty.Clear(); m_Warnings.Clear();
    }

    protected override void OnUpdate()
    {
        if (GameManager.instance.gameMode != GameMode.Game) return;
        var enabled = Mod.Settings.ShowRoadRestrictionSigns;
        var custom = Mod.Settings.RoadSignPrefabMode == "CUSTOM" ? Mod.Settings.CustomRoadSignPrefab ?? string.Empty : string.Empty;
        var settingChanged = enabled != m_Enabled || custom != m_Custom;
        if (m_CatalogDirty)
        {
            if (m_PrefabQuery.IsEmptyIgnoreFilter) return;
            m_CatalogDirty = false;
            BuildCatalog();
            settingChanged = true;
        }
        if (m_LoadDirty || settingChanged)
        {
            m_LoadDirty = false;
            m_Enabled = enabled; m_Custom = custom;
            m_Theme = World.GetOrCreateSystemManaged<CityConfigurationSystem>().defaultTheme;
            var automatic = ResolveAuto(m_Theme);
            ResolvedName = automatic?.Name ?? string.Empty;
            CustomUnavailable = custom.Length > 0 && !m_Prefabs.ContainsKey(custom);
            ClearOwned();
            if (enabled)
            {
                using var targets = m_RestrictionQuery.ToEntityArray(Allocator.Temp);
                foreach (var target in targets) MarkDirty(target);
            }
        }
        if (!enabled) { m_Dirty.Clear(); return; }
        if (m_Dirty.Count == 0) return;
        var theme = World.GetOrCreateSystemManaged<CityConfigurationSystem>().defaultTheme;
        if (theme != m_Theme)
        {
            m_Theme = theme;
            ResolvedName = ResolveAuto(theme)?.Name ?? string.Empty;
            foreach (var target in m_Markers.Keys) MarkDirty(target);
        }
        var dirty = m_Dirty.ToArray(); m_Dirty.Clear();
        foreach (var target in dirty)
        {
            try { Rebuild(target); }
            catch (Exception error) { WarnOnce(target + ":" + error.GetType().Name, $"target={target} skipped: {error.Message}"); }
        }
    }

    private void BuildCatalog()
    {
        m_Prefabs.Clear();
        using var entities = m_PrefabQuery.ToEntityArray(Allocator.Temp);
        var mask = TrafficSignData.GetTypeMask(TrafficSignType.DoNotEnter);
        foreach (var entity in entities)
        {
            var sign = EntityManager.GetComponentData<TrafficSignData>(entity);
            if (sign.m_TypeMask != mask || !m_PrefabSystem.TryGetPrefab<StaticObjectPrefab>(entity, out var prefab) ||
                !EntityManager.TryGetComponent(entity, out ObjectGeometryData geometry) || prefab.m_Meshes == null || prefab.m_Meshes.Length == 0 ||
                EntityManager.HasComponent<BuildingData>(entity) || EntityManager.HasComponent<VehicleData>(entity) || EntityManager.HasComponent<TreeData>(entity) ||
                !math.all(math.isfinite(geometry.m_Bounds.min)) || !math.all(math.isfinite(geometry.m_Bounds.max)) ||
                (EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<Game.Prefabs.SubObject> subObjects) && subObjects.Length > 0) ||
                EntityManager.HasComponent<PlaceholderObjectData>(entity)) continue;
            var components = new HashSet<ComponentType>();
            prefab.GetArchetypeComponents(components);
            components.Add(ComponentType.ReadWrite<Created>());
            components.Add(ComponentType.ReadWrite<Updated>());
            components.Add(ComponentType.ReadWrite<BatchesUpdated>());
            components.Add(ComponentType.ReadWrite<RoadRestrictionSignOwner>());
            var theme = Entity.Null;
            if (prefab.TryGet<ThemeObject>(out var themeObject) && themeObject.m_Theme != null)
                m_PrefabSystem.TryGetEntity(themeObject.m_Theme, out theme);
            if (theme == Entity.Null && EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<ObjectRequirementElement> requirements))
                foreach (var requirement in requirements)
                    if (EntityManager.HasComponent<ThemeData>(requirement.m_Requirement)) { theme = requirement.m_Requirement; break; }
            m_Prefabs[prefab.name] = new SignPrefab { Entity = entity, Theme = theme, Name = prefab.name,
                Icon = ImageSystem.GetIcon(prefab) ?? string.Empty,
                Archetype = EntityManager.CreateArchetype(components.ToArray()), GroundOffset = -math.min(0f, geometry.m_Bounds.min.y) };
            Mod.Log.Info($"[RouteFilter.RoadSigns] prefab={prefab.name} theme={(theme == Entity.Null ? "generic" : m_PrefabSystem.GetPrefabName(theme))} nativeIcon={ImageSystem.GetIcon(prefab) != null}");
        }
        Catalog = string.Join("\n", m_Prefabs.Values.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Encode(p.Name) + "|" + Encode(p.Icon)));
        Mod.Log.Info($"[RouteFilter.RoadSigns] compatiblePrefabs={m_Prefabs.Count} nativeType=DoNotEnter");
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private SignPrefab ResolveAuto(Entity theme) => m_Prefabs.Values.Where(p => p.Theme == theme && theme != Entity.Null)
        .OrderBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault()
        ?? m_Prefabs.Values.Where(p => p.Theme == Entity.Null).OrderBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault();

    private void Rebuild(Entity target)
    {
        RemoveTarget(target);
        var entries = m_Index.GetAppliedRoadEntries(target);
        if (entries.Count == 0) return;
        var markers = new List<Entity>();
        m_Markers[target] = markers;
        Watch(target, target);
        foreach (var entry in entries)
        {
            Watch(target, entry.Connection);
            foreach (var gate in entry.Gates) { Watch(target, gate.m_EntryLane); Watch(target, gate.m_NextLane); }
            if (!entry.Enabled) continue;
            var prefab = m_Custom != null && m_Prefabs.TryGetValue(m_Custom, out var selected) ? selected : ResolveAuto(m_Theme);
            if (prefab == null) { WarnOnce("prefab:" + m_Theme, "No compatible No Entry prefab for the actual theme; enforcement unchanged"); continue; }
            if (!m_Index.TryGetApproachFrame(entry, true, out var center, out var forward, out var low, out var high))
            { WarnOnce("geometry:" + target, $"target={target} entering lane width/direction unavailable; visual skipped"); continue; }
            if (!RoadSignPlacement.TryCreate(center, forward, low, high, prefab.GroundOffset, out var first, out var second, out var rotation))
            { WarnOnce("placement:" + target, $"target={target} invalid entry frame; visual skipped"); continue; }
            markers.Add(CreateMarker(target, prefab, first, rotation));
            markers.Add(CreateMarker(target, prefab, second, rotation));
        }
        Mod.Log.Debug($"[RouteFilter.RoadSigns] target={target} signs={markers.Count}");
    }

    private Entity CreateMarker(Entity target, SignPrefab prefab, float3 position, quaternion rotation)
    {
        var entity = EntityManager.CreateEntity(prefab.Archetype);
        EntityManager.SetComponentData(entity, new PrefabRef(prefab.Entity));
        EntityManager.SetComponentData(entity, new ObjectTransform(position, rotation));
        EntityManager.SetComponentData(entity, new RoadRestrictionSignOwner { Target = target });
        MarkerCount++;
        return entity;
    }

    private void RemoveTarget(Entity target)
    {
        if (m_WatchedByTarget.TryGetValue(target, out var watched))
        {
            foreach (var entity in watched)
                if (m_TargetsByWatch.TryGetValue(entity, out var targets) && targets.Remove(target) && targets.Count == 0)
                {
                    m_TargetsByWatch.Remove(entity);
                    if (EntityManager.Exists(entity)) EntityManager.RemoveComponent<RoadRestrictionSignGeometryWatch>(entity);
                }
            m_WatchedByTarget.Remove(target);
        }
        if (!m_Markers.TryGetValue(target, out var markers)) return;
        foreach (var entity in markers)
            if (EntityManager.Exists(entity) && EntityManager.TryGetComponent(entity, out RoadRestrictionSignOwner owner) && owner.Target == target)
            {
                EntityManager.AddComponent<Deleted>(entity);
                EntityManager.AddComponent<BatchesUpdated>(entity);
                MarkerCount--;
            }
        m_Markers.Remove(target);
    }

    private void Watch(Entity target, Entity entity)
    {
        if (entity == Entity.Null || !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)) return;
        if (!m_WatchedByTarget.TryGetValue(target, out var watched)) m_WatchedByTarget[target] = watched = new();
        if (!watched.Add(entity)) return;
        if (!m_TargetsByWatch.TryGetValue(entity, out var targets))
        {
            m_TargetsByWatch[entity] = targets = new();
            EntityManager.AddComponent<RoadRestrictionSignGeometryWatch>(entity);
        }
        targets.Add(target);
    }

    public void ClearOwned()
    {
        foreach (var target in m_Markers.Keys.ToArray()) RemoveTarget(target);
        MarkerCount = 0;
    }

    internal void ExcludeFromSave()
    {
        if (m_OwnedQuery.IsEmptyIgnoreFilter) return;
        EntityManager.AddComponent<Temp>(m_OwnedQuery);
        m_SaveExcluded = true;
    }

    internal void EndSave()
    {
        if (!m_SaveExcluded) return;
        EntityManager.RemoveComponent<Temp>(m_OwnedQuery);
        m_SaveExcluded = false;
    }

    private void WarnOnce(string key, string text) { if (m_Warnings.Count < 128 && m_Warnings.Add(key)) Mod.Log.Warn("[RouteFilter.RoadSigns] " + text); }
}

public sealed class RoadRestrictionSignSaveGuardSystem : GameSystemBase
{
    protected override void OnUpdate() => World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.ExcludeFromSave();
}

public sealed class RoadRestrictionSignGeometryChangedSystem : GameSystemBase
{
    protected override void OnUpdate() => World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.CollectGeometryChanges();
}

public sealed class RoadRestrictionSignSaveFinishSystem : GameSystemBase
{
    protected override void OnUpdate() => World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.EndSave();
}
