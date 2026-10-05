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
    internal Entity Connection;
    internal int EntryOrdinal;
}

internal struct RoadRestrictionSignGeometryWatch : IComponentData { }

public sealed partial class RoadRestrictionVisualSignsSystem : GameSystemBase
{
    internal sealed class SignPrefab
    {
        internal Entity Entity;
        internal Entity Theme;
        internal string Name;
        internal string Icon;
        internal EntityArchetype Archetype;
        internal float GroundOffset;
        internal bool IsNoEntry;
    }

    private readonly Dictionary<string, SignPrefab> m_Prefabs = new(StringComparer.Ordinal);
    private readonly Dictionary<Entity, List<Entity>> m_Markers = new();
    private readonly Dictionary<Entity, List<Entity>> m_VisualAnchors = new();
    private readonly Dictionary<Entity, HashSet<Entity>> m_WatchedByTarget = new();
    private readonly Dictionary<Entity, HashSet<Entity>> m_TargetsByWatch = new();
    private readonly Dictionary<Entity, uint> m_GeometryStamps = new();
    private readonly HashSet<Entity> m_Dirty = new();
    // Checked once on the next visual update, after native rendering has run.
    private readonly List<Entity> m_RenderChecks = new();
    private readonly HashSet<string> m_Warnings = new();
    private EntityQuery m_PrefabQuery;
    private EntityQuery m_OwnedQuery;
    private EntityQuery m_RestrictionQuery;
    private EntityQuery m_ChangedGeometryQuery;
    private PrefabSystem m_PrefabSystem;
    private RestrictionIndexSystem m_Index;
    private readonly List<Entity> m_RetiredAnchors = new();
    private bool m_ClearRequested;
    private bool m_CatalogDirty = true;
    private bool m_LoadDirty;
    private bool m_Enabled;
    private bool m_SaveExcluded;
    private string m_Custom = null;
    private Entity m_Theme;
    private string m_Profile;
    private readonly Dictionary<Entity,string> m_ThemePrefixes = new();
    private readonly Dictionary<Entity,string> m_TargetProfiles = new();
    private readonly Dictionary<Entity,bool> m_TargetFallback = new();
    public string ResolvedProfile { get; private set; } = "GENERIC_EUROPE";
    public bool ProfileFallback { get; private set; }
    internal string ProfileFor(Entity target) => m_TargetProfiles.TryGetValue(target,out var value) ? value : ResolvedProfile;
    internal bool ProfileFallbackFor(Entity target) => m_TargetFallback.TryGetValue(target,out var value) ? value : ProfileFallback;
    private string ThemePrefix(Entity theme)
    {
        if(m_ThemePrefixes.TryGetValue(theme,out var prefix)) return prefix;
        prefix = theme!=Entity.Null && m_PrefabSystem.TryGetPrefab<ThemePrefab>(theme,out var prefab) ? prefab.assetPrefix ?? "" : "";
        m_ThemePrefixes[theme]=prefix; return prefix;
    }
    private string ResolveProfile(Entity theme) => SignageProfiles.Resolve(Mod.Settings.SignageProfile,ThemePrefix(theme),ActiveSignLocale);
    private SignPrefab ResolveProfilePrefab(string profile,Entity roadTheme,out bool fallback)
    {
        var family=profile=="US"?"NA":"EU";
        // CN/UK have no independently verified national primary capability in the native catalog.
        var compatible=m_Prefabs.Values.Where(p=>p.IsNoEntry && ThemePrefix(p.Theme)==family).OrderBy(p=>p.Name,StringComparer.Ordinal).FirstOrDefault();
        fallback=profile=="CN" || profile=="UK" || compatible==null;
        return compatible ?? ResolveAuto(roadTheme);
    }

    public string Catalog { get; private set; } = string.Empty;
    public string ResolvedName { get; private set; } = string.Empty;
    public bool CustomUnavailable { get; private set; }
    public int MarkerCount { get; private set; }
    public bool ContainsPrefab(string name) => m_Prefabs.ContainsKey(name);
    internal void InvalidateVehicleLabels() { m_VehicleCatalogRevision++; m_VehicleSemantics.Clear(); m_LoadDirty = true; }
    internal void InvalidateAppearance() => m_LoadDirty = true;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<StaticObjectData>(), ComponentType.ReadOnly<ObjectGeometryData>(), ComponentType.ReadOnly<PrefabData>());
        m_OwnedQuery = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<RoadRestrictionSignOwner>() }, None = new[] { ComponentType.ReadOnly<Deleted>() } });
        m_RestrictionQuery = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_ChangedGeometryQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<RoadRestrictionSignGeometryWatch>() },
            Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
        });
        m_PrefabSystem.onContentAvailabilityChanged += ContentChanged;
        GameManager.instance.onGameLoadingComplete += Loaded;
        GameManager.instance.localizationManager.onActiveDictionaryChanged += LocaleChanged;
    }

    protected override void OnDestroy()
    {
        m_PrefabSystem.onContentAvailabilityChanged -= ContentChanged;
        if (GameManager.instance != null) GameManager.instance.onGameLoadingComplete -= Loaded;
        if (GameManager.instance != null) GameManager.instance.localizationManager.onActiveDictionaryChanged -= LocaleChanged;
        ClearOwned();
        m_Resources.Dispose();
        base.OnDestroy();
    }

    private void ContentChanged() => m_CatalogDirty = true;
    private void Loaded(Purpose purpose, GameMode mode) { m_LoadDirty = true; m_CatalogDirty = true; m_TargetProfiles.Clear(); m_TargetFallback.Clear(); }
    public void MarkDirty(Entity target) { if (target != Entity.Null) m_Dirty.Add(target); }

    internal void CollectGeometryChanges()
    {
        if (m_ChangedGeometryQuery.IsEmptyIgnoreFilter) return;
        using var changed = m_ChangedGeometryQuery.ToEntityArray(Allocator.Temp);
        foreach (var entity in changed)
        {
            var stamp = VisualGeometryStamp.Read(EntityManager,entity);
            if (m_GeometryStamps.TryGetValue(entity,out var previous) && stamp == previous) continue;
            m_GeometryStamps[entity] = stamp;
            if (m_TargetsByWatch.TryGetValue(entity, out var targets))
                foreach (var target in targets)
                {
                    MarkDirty(target);
                    World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.MarkDirty(target);
                }
        }
    }

    public void ResetRuntimeState()
    {
        // Reset is requested from UIUpdate; native visual mutations run in Modification4.
        m_ClearRequested = true; m_Dirty.Clear(); m_Warnings.Clear(); m_RenderChecks.Clear(); m_LabelCache.Clear(); m_TargetProfiles.Clear(); m_TargetFallback.Clear();
    }

    protected override void OnUpdate()
    {
        if (GameManager.instance.gameMode != GameMode.Game) return;
        if (m_ClearRequested) { m_ClearRequested = false; ClearOwned(); }
        RetireVisualAnchors();
        CheckInitializedMarkers();
        var enabled = Mod.Settings.ShowRoadRestrictionSigns;
        var custom = Mod.Settings.RoadSignPrefabMode == "CUSTOM" ? Mod.Settings.CustomRoadSignPrefab ?? string.Empty : string.Empty;
        var appearanceChanged = RefreshAppearance();
        var settingChanged = enabled != m_Enabled || custom != m_Custom || m_Profile != Mod.Settings.SignageProfile;
        if (appearanceChanged)
        {
            var previewTarget = World.GetExistingSystemManaged<RouteFilterUISystem>()?.AppearancePreviewTarget ?? Entity.Null;
            if (previewTarget != Entity.Null) MarkDirty(previewTarget);
            else foreach (var target in m_Markers.Keys) MarkDirty(target);
        }
        if (m_TopologyRevision != m_Index.Revision)
        {
            m_TopologyRevision = m_Index.Revision;
            foreach (var target in m_Markers.Keys) MarkDirty(target);
            using var targets = m_RestrictionQuery.ToEntityArray(Allocator.Temp);
            foreach (var target in targets) MarkDirty(target);
        }
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
            m_Enabled = enabled; m_Custom = custom; m_Profile=Mod.Settings.SignageProfile;
            Mod.Log.Info($"[RouteFilter.RoadSigns] visualConfig enabled={enabled} mode={(custom.Length == 0 ? "AUTO" : "CUSTOM")} custom={custom} phase=Modification4");
            m_Theme = World.GetOrCreateSystemManaged<CityConfigurationSystem>().defaultTheme;
            ResolvedProfile=ResolveProfile(m_Theme);
            var automatic = ResolveProfilePrefab(ResolvedProfile,m_Theme,out var fallback); ProfileFallback=fallback;
            ResolvedName = automatic?.Name ?? string.Empty;
            CustomUnavailable = custom.Length > 0 && !m_Prefabs.ContainsKey(custom);
            if (!enabled) ClearOwned();
            if (enabled)
            {
                using var targets = m_RestrictionQuery.ToEntityArray(Allocator.Temp);
                foreach (var target in targets) MarkDirty(target);
            }
        }
        if (!enabled) { m_Dirty.Clear(); return; }
        var theme = World.GetOrCreateSystemManaged<CityConfigurationSystem>().defaultTheme;
        if (theme != m_Theme)
        {
            m_Theme = theme;
            ResolvedProfile=ResolveProfile(theme);
            ResolvedName = ResolveProfilePrefab(ResolvedProfile,theme,out var fallback)?.Name ?? string.Empty; ProfileFallback=fallback;
            foreach (var target in m_Markers.Keys) MarkDirty(target);
        }
        if (m_Dirty.Count == 0) return;
        var dirty = m_Dirty.ToArray(); m_Dirty.Clear();
        foreach (var target in dirty)
        {
            try { Rebuild(target); }
            catch (Exception error) { WarnOnce(target + ":" + error.GetType().Name, $"target={target} skipped: {error.Message}"); }
        }
    }

    private void BuildCatalog()
    {
        ClearOwned(); m_Resources.Dispose(); m_Resources = new();
        m_Prefabs.Clear(); m_ThemePrefixes.Clear();
        using var entities = m_PrefabQuery.ToEntityArray(Allocator.Temp);
        var mask = TrafficSignData.GetTypeMask(TrafficSignType.DoNotEnter);
        foreach (var entity in entities)
        {
            if (!EntityManager.TryGetComponent(entity,out TrafficSignData sign) || sign.m_TypeMask==0) continue;
            var isNoEntry = (sign.m_TypeMask & mask) != 0;
            if (!m_PrefabSystem.TryGetPrefab<StaticObjectPrefab>(entity, out var prefab) ||
                !EntityManager.TryGetComponent(entity, out ObjectGeometryData geometry) || prefab.m_Meshes == null || prefab.m_Meshes.Length == 0 ||
                EntityManager.HasComponent<BuildingData>(entity) || EntityManager.HasComponent<VehicleData>(entity) || EntityManager.HasComponent<TreeData>(entity) ||
                !math.all(math.isfinite(geometry.m_Bounds.min)) || !math.all(math.isfinite(geometry.m_Bounds.max)) ||
                (EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<Game.Prefabs.SubObject> subObjects) && subObjects.Length > 0) ||
                EntityManager.HasComponent<PlaceholderObjectData>(entity)) continue;
            var components = new HashSet<ComponentType>();
            prefab.GetArchetypeComponents(components);
            // Native BatchInstanceSystem requires Clear/Forward mesh states from NetObject.
            // This instance component does not write network composition or lane blockage.
            if (EntityManager.HasComponent<NetObjectData>(entity)) components.Add(ComponentType.ReadWrite<Game.Objects.NetObject>());
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
                IsNoEntry = isNoEntry, Archetype = EntityManager.CreateArchetype(components.ToArray()), GroundOffset = -math.min(0f, geometry.m_Bounds.min.y) };
            if (isNoEntry) Mod.Log.Info($"[RouteFilter.RoadSigns] prefab={prefab.name} theme={(theme == Entity.Null ? "generic" : m_PrefabSystem.GetPrefabName(theme))} nativeIcon={ImageSystem.GetIcon(prefab) != null}");
            if (isNoEntry && EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<SubMesh> meshes))
            {
                var flags = new List<string>();
                foreach (var mesh in meshes) flags.Add(mesh.m_Flags.ToString());
                Mod.Log.Info($"[RouteFilter.RoadSigns] renderContract prefab={prefab.name} NetObjectData={EntityManager.HasComponent<NetObjectData>(entity)} layers={geometry.m_Layers} minLod={geometry.m_MinLod} meshFlags={string.Join(",", flags)}");
            }
        }
        Catalog = string.Join("\n", m_Prefabs.Values.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Encode(p.Name) + "|" + Encode(p.Icon)));
        Mod.Log.Info($"[RouteFilter.RoadSigns] compatiblePrefabs={m_Prefabs.Count} customType=RenderableStaticObject autoType=DoNotEnter");
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private SignPrefab ResolveAuto(Entity theme) => m_Prefabs.Values.Where(p => p.IsNoEntry && p.Theme == theme && theme != Entity.Null)
        .OrderBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault()
        ?? m_Prefabs.Values.Where(p => p.IsNoEntry && p.Theme == Entity.Null).OrderBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault()
        ?? m_Prefabs.Values.Where(p => p.IsNoEntry).OrderBy(p => p.Name,StringComparer.Ordinal).FirstOrDefault();

    private SignPrefab ResolveEntryPrefab(LogicalEntryGroup entry,out string profile,out bool fallback)
    {
        var theme = m_Theme;
        if (EntityManager.TryGetComponent(entry.Connection,out PrefabRef reference) &&
            m_PrefabSystem.TryGetPrefab<PrefabBase>(reference.m_Prefab,out var road) &&
            road.TryGet<ThemeObject>(out var themed) && themed.m_Theme != null)
            m_PrefabSystem.TryGetEntity(themed.m_Theme,out theme);
        profile=ResolveProfile(theme);
        var automatic=ResolveProfilePrefab(profile,theme,out fallback);
        if (m_Custom != null && m_Prefabs.TryGetValue(m_Custom,out var custom)) return custom;
        return automatic;
    }

    private void Rebuild(Entity target)
    {
        if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target) ||
            !EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> saved) || saved.Length == 0)
        { RemoveTarget(target); return; }
        var meanings = Meanings(target);
        m_TargetProfiles.Remove(target); m_TargetFallback.Remove(target);
        var entries = m_Index.GetAppliedRoadEntries(target);
        // Selection/Updated may precede native lane readiness. Keep the last good
        // physical assembly until the replacement has a usable geometry source.
        if (m_Markers.ContainsKey(target) && (entries.Count == 0 || entries.Any(entry => entry.Enabled &&
            !m_Index.TryGetApproachFrame(entry, true, out _, out _, out _, out _))))
        { Mod.Log.Info($"[RouteFilter.RoadSigns] retained previous assembly; target={target} entries={entries.Count} geometry pending"); return; }
        RemoveTarget(target);
        var restricted = 0; var pairs = 0; var skipped = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var markers = new List<Entity>();
        if (entries.Count > 0) { m_Markers[target] = markers; Watch(target, target); }
        var prefab = m_Custom != null && m_Prefabs.TryGetValue(m_Custom, out var selected) ? selected : ResolveAuto(m_Theme);
        void Skip(string reason, string detail, int markerCount = 2)
        {
            skipped += markerCount;
            reasons.TryGetValue(reason, out var count); reasons[reason] = count + markerCount;
            Mod.Log.Info($"[RouteFilter.RoadSigns] target={target} SkipReason={reason} {detail}");
        }
        var ordinal = 0;
        foreach (var entry in entries)
        {
            ordinal++;
            Watch(target, entry.Connection);
            foreach (var gate in entry.Gates) { Watch(target, gate.m_EntryLane); Watch(target, gate.m_NextLane); }
            if (!entry.Enabled) continue;
            prefab = ResolveEntryPrefab(entry,out var profile,out var profileFallback);
            if (m_TargetProfiles.TryGetValue(target,out var previousProfile) && previousProfile!=profile) m_TargetProfiles[target]="MIXED";
            else m_TargetProfiles[target]=profile;
            m_TargetFallback[target]=profileFallback || (m_TargetFallback.TryGetValue(target,out var previousFallback) && previousFallback);
            // The native no-entry capability is verified. No inferred category pictograms.
            var primary= prefab!=null && prefab.IsNoEntry ? TrafficVehicleSemantic.AllRoadMotorVehicles : (TrafficVehicleSemantic?)null;
            var customActive=m_Custom!=null && m_Prefabs.ContainsKey(m_Custom);
            var legends=TrafficSignSemantics.PrimaryFullyExpresses(meanings,primary,customActive && !prefab.IsNoEntry)?Array.Empty<TrafficLegend>():meanings;
            restricted++;
            if (prefab == null) { Skip("NO_PREFAB", $"theme={m_Theme}"); continue; }
            if (!EntityManager.Exists(prefab.Entity) || !EntityManager.HasComponent<ObjectGeometryData>(prefab.Entity))
            { Skip("NO_RENDER_PREFAB", $"prefab={prefab.Name}"); continue; }
            if (!m_Index.TryGetApproachFrame(entry, true, out var center, out var forward, out var low, out var high))
            { Skip("NO_ENTRY_GEOMETRY", DescribeGeometryFailure(entry)); continue; }
            GetSignMargins(entry, center, forward, low, high, out var leftMargin, out var rightMargin);
            leftMargin = math.max(0,leftMargin + m_Lateral); rightMargin = math.max(0,rightMargin + m_Lateral);
            if (!RoadSignPlacement.TryCreate(center, forward, low, high, prefab.GroundOffset, out var first, out var second, out var rotation, leftMargin, rightMargin))
            { Skip("INVALID_TRANSFORM", $"connection={entry.Connection} center={center} forward={forward} bounds={low}/{high}"); continue; }
            pairs++;
            var longitudinal = math.normalizesafe(new float3(forward.x,0,forward.z)) * m_Longitudinal;
            first += longitudinal; second += longitudinal;
            rotation = math.mul(rotation,quaternion.RotateY(math.radians(m_Rotation)));
            var geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab.Entity);
            // A prohibition face is approximately as tall as it is wide; exclude its pole.
            var faceWidth = math.max(geometry.m_Bounds.max.x-geometry.m_Bounds.min.x,geometry.m_Bounds.max.z-geometry.m_Bounds.min.z);
            var mainBottom = math.max(.4f,(geometry.m_Bounds.max.y-faceWidth)*m_Scale);
            var firstPlate = SignAppearance.FirstPlateHeight(mainBottom, legends.Length, m_PlateSpacing);
            var lift = math.max(0,firstPlate + .125f + .18f - mainBottom) + m_Height;
            first.y += lift; second.y += lift;
            // Entries come from the same derived topology as Directional Restrictions.
            // Only visual instances are created; no native network/blocked-lane components.
            var before = markers.Count;
            try
            {
                var anchor = CreateVisualAnchor(target,target,prefab,first,rotation,entry.Connection,ordinal);
                var leftHand = World.GetOrCreateSystemManaged<CityConfigurationSystem>().leftHandTraffic;
                var roadside = leftHand ? first : second;
                var main = CreateMarker(target, anchor, prefab, roadside, rotation, entry.Connection,ordinal); markers.Add(main);
                CreateAssembly(target,main,prefab,roadside,rotation,legends,profile,firstPlate-lift+m_Height);
                // Repeat only on wide approaches, as a gameplay visibility adjustment.
                if (RoadSignPlacement.RepeatOppositeSide(low,high))
                {
                    var opposite = leftHand ? second : first;
                    var repeat = CreateMarker(target, anchor, prefab, opposite, rotation, entry.Connection,ordinal); markers.Add(repeat);
                    CreateAssembly(target,repeat,prefab,opposite,rotation,legends,profile,firstPlate-lift+m_Height);
                }
                var gate = entry.Gates[0];
                Mod.Log.Info($"[RouteFilter.RoadSigns] target={target} connection={entry.Connection} EntryLane={gate.m_EntryLane} NextLane={gate.m_NextLane} LEFT={first} RIGHT={second} Rotation={rotation.value} Scale=1 edgeMargins={leftMargin}/{rightMargin} laneBounds={low}/{high} prefab={prefab.Name}");
            }
            catch (Exception error) { Skip("ENTITY_CREATION_FAILED", $"connection={entry.Connection} error={error}", 2 - (markers.Count - before)); }
        }
        var emptyReason = entries.Count != 0 ? string.Empty : ExplainEmptyTarget(target);
        Mod.Log.Info($"[RouteFilter.RoadSigns] Target={target} LogicalEntries={entries.Count} RestrictedEntries={restricted} ResolvedSignPrefab={prefab?.Name ?? "NONE"} ResolvedPrefabEntity={prefab?.Entity.ToString() ?? "NONE"} PlacementPairs={pairs} CreatedMarkers={markers.Count} SkippedMarkers={skipped} SkipReasons={string.Join(",", reasons.Select(r => r.Key + "=" + r.Value))}{emptyReason} phase=Modification4");
    }

    private void GetSignMargins(LogicalEntryGroup entry, float3 center, float3 travel, float low, float high,
        out float leftMargin, out float rightMargin)
    {
        leftMargin = rightMargin = .8f;
        // Local, rebuild-only geometry check; no topology or enforcement changes.
        if (!EntityManager.TryGetBuffer(entry.Connection, true, out DynamicBuffer<Game.Net.SubLane> lanes)) return;
        var lateral = new float3(-travel.z, 0f, travel.x);
        foreach (var sub in lanes)
        {
            var lane = sub.m_SubLane;
            if (!EntityManager.HasComponent<Game.Net.CarLane>(lane) || EntityManager.HasComponent<Game.Net.MasterLane>(lane) ||
                !EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) ||
                !EntityManager.TryGetComponent(lane, out PrefabRef prefab) ||
                !EntityManager.TryGetComponent(prefab.m_Prefab, out NetLaneData data) ||
                !math.isfinite(data.m_Width) || data.m_Width <= .1f) continue;
            Colossal.Mathematics.MathUtils.Distance(curve.m_Bezier, center, out var t);
            var point = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, t);
            var direction = math.normalizesafe(Colossal.Mathematics.MathUtils.Tangent(curve.m_Bezier, t));
            if (math.dot(direction, travel) > -.5f || math.abs(math.dot(point - center, travel)) > 2f) continue;
            var offset = math.dot(point - center, lateral);
            RoadSignPlacement.ConstrainToDivider(low, high, offset - data.m_Width * .5f,
                offset + data.m_Width * .5f, ref leftMargin, ref rightMargin);
        }
    }

    private string ExplainEmptyTarget(Entity target)
    {
        if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target)) return " EmptyReason=TARGET_MISSING_OR_DELETED";
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets) || assets.Length == 0) return " EmptyReason=NO_RESTRICTION";
        foreach (var asset in assets)
            if (EntityManager.HasComponent<CarData>(asset.m_Prefab)) return " EmptyReason=NO_DERIVED_ROAD_ENTRIES";
        return " EmptyReason=NO_ROAD_PREFAB_RESTRICTION";
    }

    private string DescribeGeometryFailure(LogicalEntryGroup entry)
    {
        if (entry.Gates.Count == 0) return $"connection={entry.Connection} invariant=NO_GATES";
        var node = entry.Gates[0].m_TargetEndpoint == RestrictionEndpoint.None;
        foreach (var gate in entry.Gates)
        {
            var lane = node ? gate.m_EntryLane : gate.m_NextLane;
            if (!EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) || curve.m_Length <= .1f)
                return $"lane={lane} invariant=MISSING_OR_DEGENERATE_CURVE";
            if (!EntityManager.TryGetComponent(lane, out PrefabRef prefab) ||
                !EntityManager.TryGetComponent(prefab.m_Prefab, out NetLaneData data)) return $"lane={lane} invariant=MISSING_NATIVE_LANE_WIDTH";
            if (!math.isfinite(data.m_Width) || data.m_Width <= .1f) return $"lane={lane} invariant=INVALID_NATIVE_LANE_WIDTH width={data.m_Width}";
        }
        return $"connection={entry.Connection} invariant=ZERO_OR_NONFINITE_COMBINED_TANGENT";
    }

    private Entity CreateVisualAnchor(Entity target, Entity road,SignPrefab prefab,float3 position,quaternion rotation,Entity connection,int ordinal)
    {
        // A private visual assembly root. No Object/Net entity and no road buffer writes.
        // Native OverrideSystem follows the child Owner to Attached on its parent and
        // exempts that road from prop collision. SubObject references stay on this root.
        var anchor = EntityManager.CreateEntity(ComponentType.ReadWrite<Game.Objects.Attached>(),
            ComponentType.ReadWrite<RoadRestrictionSignOwner>());
        EntityManager.SetComponentData(anchor, new Game.Objects.Attached(road, Entity.Null, 0f));
        EntityManager.SetComponentData(anchor, new RoadRestrictionSignOwner { Target = target,Connection = connection,EntryOrdinal = ordinal });
        // Native selection may promote a sub-object hit to its owning assembly.
        // Give that visual root a name/position source without making it a road.
        EntityManager.AddComponentData(anchor,new PrefabRef(prefab.Entity));
        EntityManager.AddComponentData(anchor,new ObjectTransform(position,rotation));
        EntityManager.AddBuffer<Game.Objects.SubObject>(anchor);
        if (!m_VisualAnchors.TryGetValue(target, out var anchors)) m_VisualAnchors[target] = anchors = new();
        anchors.Add(anchor);
        return anchor;
    }

    private Entity CreateMarker(Entity target, Entity anchor, SignPrefab prefab, float3 position, quaternion rotation, Entity connection,int ordinal)
    {
        var entity = EntityManager.CreateEntity(prefab.Archetype);
        try
        {
            EntityManager.SetComponentData(entity, new PrefabRef(prefab.Entity));
            EntityManager.SetComponentData(entity, new ObjectTransform(position, rotation));
            EntityManager.SetComponentData(entity, new RoadRestrictionSignOwner { Target = target, Connection = connection,EntryOrdinal = ordinal });
            EntityManager.AddComponentData(entity, new Owner(anchor));
            if (EntityManager.HasComponent<Game.Objects.NetObject>(entity))
                EntityManager.SetComponentData(entity, new Game.Objects.NetObject());
            if (EntityManager.HasComponent<PseudoRandomSeed>(entity))
                EntityManager.SetComponentData(entity, new PseudoRandomSeed((ushort)(1 + (uint)entity.Index % 65534)));
            if (!EntityManager.HasComponent<Game.Objects.Object>(entity) || !EntityManager.HasComponent<Game.Objects.Static>(entity) ||
                !EntityManager.HasComponent<CullingInfo>(entity) || !EntityManager.HasBuffer<MeshBatch>(entity))
                throw new InvalidOperationException("Static prefab lacks native object/culling/batch instance components");
            EntityManager.GetBuffer<Game.Objects.SubObject>(anchor).Add(new Game.Objects.SubObject(entity));
            MarkerCount++;
            m_RenderChecks.Add(entity);
            return entity;
        }
        catch { EntityManager.DestroyEntity(entity); throw; }
    }

    private void CheckInitializedMarkers()
    {
        if (m_RenderChecks.Count == 0) return;
        foreach (var entity in m_RenderChecks)
        {
            if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)) continue;
            var owner = EntityManager.GetComponentData<RoadRestrictionSignOwner>(entity);
            var culling = EntityManager.GetComponentData<CullingInfo>(entity);
            var batches = EntityManager.GetBuffer<MeshBatch>(entity, true);
            Mod.Log.Info($"[RouteFilter.RoadSigns] renderInitialization Target={owner.Target} marker={entity} CullingRadius={culling.m_Radius} CullingBounds={culling.m_Bounds.min}/{culling.m_Bounds.max} CullingIndex={culling.m_CullingIndex} CullingMask={culling.m_Mask} PassedCulling={culling.m_PassedCulling} MinLod={culling.m_MinLod} Marker={EntityManager.HasComponent<Game.Objects.Marker>(entity)} Overridden={EntityManager.HasComponent<Overridden>(entity)} Owner={EntityManager.GetComponentData<Owner>(entity).m_Owner} NetObject={EntityManager.HasComponent<Game.Objects.NetObject>(entity)} MeshBatches={batches.Length} visibility=REQUIRES_GAME_CHECK");
        }
        m_RenderChecks.Clear();
    }

    private void RemoveTarget(Entity target)
    {
        if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target) || !EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target)) m_LabelCache.Remove(target);
        RemoveAssembly(target);
        if (m_WatchedByTarget.TryGetValue(target, out var watched))
        {
            foreach (var entity in watched)
                if (m_TargetsByWatch.TryGetValue(entity, out var targets) && targets.Remove(target) && targets.Count == 0)
                {
                    m_TargetsByWatch.Remove(entity);
                    m_GeometryStamps.Remove(entity);
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
        if (m_VisualAnchors.TryGetValue(target, out var anchors))
        {
            foreach (var anchor in anchors)
                if (EntityManager.Exists(anchor) && EntityManager.TryGetComponent(anchor, out RoadRestrictionSignOwner owner) && owner.Target == target)
                    m_RetiredAnchors.Add(anchor);
            m_VisualAnchors.Remove(target);
        }
    }

    private void RetireVisualAnchors()
    {
        // Keep each native Owner alive while Deleted child props are removed from
        // object searches, render batches and SubObject references by vanilla.
        for (var i = m_RetiredAnchors.Count - 1; i >= 0; i--)
        {
            var anchor = m_RetiredAnchors[i];
            if (!EntityManager.Exists(anchor)) { m_RetiredAnchors.RemoveAt(i); continue; }
            if (!EntityManager.HasComponent<RoadRestrictionSignOwner>(anchor) || !EntityManager.HasBuffer<Game.Objects.SubObject>(anchor))
            { m_RetiredAnchors.RemoveAt(i); continue; }
            var children = EntityManager.GetBuffer<Game.Objects.SubObject>(anchor, true);
            var alive = false;
            foreach (var child in children) alive |= EntityManager.Exists(child.m_SubObject);
            if (alive) continue;
            // Non-rendered private helper has no native cleanup work of its own.
            EntityManager.DestroyEntity(anchor);
            m_RetiredAnchors.RemoveAt(i);
        }
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
            m_GeometryStamps[entity] = VisualGeometryStamp.Read(EntityManager,entity);
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
    protected override void OnUpdate()
    {
        World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.CollectGeometryChanges();
        World.GetExistingSystemManaged<RouteFilterUISystem>()?.CollectMapGeometryChanges();
    }
}

public sealed class RoadRestrictionSignSaveFinishSystem : GameSystemBase
{
    protected override void OnUpdate() => World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.EndSave();
}
