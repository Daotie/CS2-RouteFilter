using Colossal.UI.Binding;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RouteFilterUISystem : UISystemBase
{
    private sealed class AssetInfo
    {
        public Entity Entity;
        public string Name = string.Empty;
        public int Mode;
        public float MaxSpeed;
        public float Acceleration;
        public float Braking;
        public Entity Parent;
        public bool IsTrailer;
    }

    private ToolSystem m_ToolSystem = null!;
    private RestrictionToolSystem m_RestrictionTool = null!;
    private PrefabSystem m_PrefabSystem = null!;
    private EntityQuery m_VehiclePrefabQuery;
    private readonly Dictionary<int, Entity> m_AssetsById = new();
    private readonly Dictionary<Entity, int> m_IdsByAsset = new();
    private readonly Dictionary<Entity, int> m_ModeByAsset = new();
    private readonly Dictionary<Entity, List<Entity>> m_ChildrenByAsset = new();
    private ValueBinding<bool> m_ToolActiveBinding = null!;
    private ValueBinding<int> m_TargetModeBinding = null!;
    private ValueBinding<int> m_TargetTransportBinding = null!;
    private ValueBinding<int> m_SelectedTargetKindBinding = null!;
    private ValueBinding<string> m_AssetCatalogBinding = null!;
    private ValueBinding<string> m_SelectedAssetsBinding = null!;
    private ValueBinding<int> m_ResetCompletedBinding = null!;
    private ValueBinding<string> m_BuildIdBinding = null!;
    private ValueBinding<bool> m_ConfigurationEditableBinding = null!;
    private int m_ResetCompleted;
    private ValueBinding<int> m_PanelCloseBinding = null!;
    private int m_PanelClose;
    private ValueBinding<string> m_SelectedTargetBinding = null!;
    private ValueBinding<int> m_RestrictionRevisionBinding = null!;
    private int m_RestrictionRevision;
    private Entity m_LastSelectedTarget = Entity.Null;
    private bool m_ContentAvailabilityDirty;
    private bool m_PendingLoadRefresh;
    private float m_NextCatalogPoll;

    public override GameMode gameMode => GameMode.GameOrEditor;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        m_RestrictionTool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_VehiclePrefabQuery = GetEntityQuery(ComponentType.ReadOnly<VehicleData>(), ComponentType.ReadOnly<PrefabData>());

        m_ToolActiveBinding = CreateValue("toolActive", false);
        m_TargetModeBinding = CreateValue("targetMode", (int)Mod.SelectedTargetMode);
        m_TargetTransportBinding = CreateValue("targetTransport", 0);
        m_SelectedTargetKindBinding = CreateValue("selectedTargetKind", 0);
        m_AssetCatalogBinding = CreateValue("assetCatalog", string.Empty);
        m_SelectedAssetsBinding = CreateValue("selectedAssetIds", string.Empty);
        m_ResetCompletedBinding = CreateValue("resetCompleted", 0);
        m_PanelCloseBinding = CreateValue("panelClose", 0);
        m_BuildIdBinding = CreateValue("buildId", Mod.BuildId);
        m_ConfigurationEditableBinding = CreateValue("configurationEditable", true);
        AddBinding(new TriggerBinding(Mod.Id, "openSettings", () => World.GetOrCreateSystemManaged<RouteFilterSettingsUISystem>().OpenSettings()));
        m_SelectedTargetBinding = CreateValue("selectedTarget", string.Empty);
        m_RestrictionRevisionBinding = CreateValue("restrictionRevision", 0);
        AddBinding(new TriggerBinding(Mod.Id, "resetRouteFilterConfirmed", () => { Mod.Log.Info("[RouteFilter.Binding] Reset confirmed"); Mod.RequestReset(); }));

        AddBinding(new TriggerBinding(Mod.Id, "toggleTool", ToggleTool));
        AddBinding(new TriggerBinding(Mod.Id, "activateTool", () => { Mod.Log.Info("[RouteFilter.Binding] activateTool"); m_RestrictionTool.Activate(); }));
        AddBinding(new TriggerBinding(Mod.Id, "deactivateTool", m_RestrictionTool.Deactivate));
        AddBinding(new TriggerBinding<int>(Mod.Id, "toggleAsset", ToggleAsset));
        AddBinding(new TriggerBinding<int, bool>(Mod.Id, "toggleAssetGroup", ToggleAssetGroup));
        AddBinding(new TriggerBinding<int>(Mod.Id, "setTargetMode", SetTargetMode));
        AddBinding(new TriggerBinding<string, bool>(Mod.Id, "setFilteredAssetSelection", SetFilteredAssetSelection));
        AddBinding(new TriggerBinding(Mod.Id, "refreshAssets", () => { Mod.Log.Info("[RouteFilter.Binding] refreshAssets received"); m_NextCatalogPoll = 0; RefreshAssetCatalog(); }));
        AddBinding(new TriggerBinding(Mod.Id, "applySelection", () => { Mod.Log.Info("[RouteFilter.Binding] applySelection received"); m_RestrictionTool.ApplySelection(); PublishRestriction(); }));
        AddBinding(new TriggerBinding(Mod.Id, "clearSelectedRestriction", () => { Mod.Log.Info("[RouteFilter.Binding] clearSelectedRestriction received"); m_RestrictionTool.ClearSelectedRestriction(); LoadSelectedTargetAssets(m_RestrictionTool.SelectedTarget); PublishRestriction(); }));
        AddBinding(new TriggerBinding(Mod.Id, "cancelSelection", m_RestrictionTool.ClearSelection));
        AddBinding(new TriggerBinding<bool>(Mod.Id, "setPointerOverUi", m_RestrictionTool.SetPointerOverUi));

        // Rebuild the catalog whenever the game announces that content (asset packs, mods) became available or was removed,
        // and once after each save finishes loading, so late-loading modded assets (for example CR400AF trains) are not missed.
        m_PrefabSystem.onContentAvailabilityChanged += OnContentAvailabilityChanged;
        GameManager.instance.onGameLoadingComplete += HandleGameLoadingComplete;
    }

    protected override void OnDestroy()
    {
        m_PrefabSystem.onContentAvailabilityChanged -= OnContentAvailabilityChanged;
        var gameManager = GameManager.instance;
        if (gameManager != null) gameManager.onGameLoadingComplete -= HandleGameLoadingComplete;
        base.OnDestroy();
    }

    private void OnContentAvailabilityChanged() => m_ContentAvailabilityDirty = true;

    private void HandleGameLoadingComplete(Purpose purpose, GameMode mode) => m_PendingLoadRefresh = true;

    /// <summary>
    /// Rebuilds the catalog once after a save finishes loading, and additionally whenever vehicle
    /// prefabs or their CarData/TrainData actually change (for example asset packs that finish
    /// streaming after the load-complete event). Nothing is rebuilt while the prefab world is
    /// unchanged, and the panel opening never triggers a rebuild.
    /// </summary>
    private void PollAssetCatalog()
    {
        if (m_VehiclePrefabQuery.IsEmptyIgnoreFilter)
        {
            // Prefabs are not available (for example while loading); force a refresh once they appear.
            return;
        }

        // Global component-order versions change for unrelated entities/archetypes.
        // Compare actual catalog membership instead; never rebuild from a global version.
        if (m_AssetsById.Count != 0 && !m_PendingLoadRefresh && !m_ContentAvailabilityDirty)
        {
            using var prefabs = m_VehiclePrefabQuery.ToEntityArray(Allocator.Temp);
            var eligible = 0;
            var changed = false;
            foreach (var prefab in prefabs)
            {
                var mode = EntityManager.HasComponent<CarData>(prefab) ? 1 :
                    EntityManager.HasComponent<TrainData>(prefab) ? 2 : 0;
                if (mode == 0) continue;
                eligible++;
                if (!m_ModeByAsset.TryGetValue(prefab, out var oldMode) || oldMode != mode) changed = true;
            }
            if (!changed && eligible == m_AssetsById.Count) return;
        }
        RefreshAssetCatalog();
    }

    protected override void OnUpdate()
    {
        // Game 1.6.0f1 can run mod OnLoad on a thread-pool continuation where the Input
        // System's Temp allocator fails; retry the key binding registration here on the
        // main thread once so the shortcut key still works in that scenario.
        Mod.RetryKeyBindings();
        if (m_ToolSystem.activeTool != m_RestrictionTool && m_RestrictionTool.SelectedTarget == Entity.Null &&
            Mod.Clear != null && Mod.Clear.WasPressedThisFrame()) NotifyPanelClose();
        m_ConfigurationEditableBinding.Update(World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable);

        // Sample catalog membership every five real seconds, independently of paused FPS.
        // Explicit refresh still rebuilds immediately; unchanged catalogs never reserialize.
        P0Diagnostics.Poll(World);
        if (P0Diagnostics.Catalog && UnityEngine.Time.realtimeSinceStartup >= m_NextCatalogPoll)
        {
            m_NextCatalogPoll = UnityEngine.Time.realtimeSinceStartup + 5f;
            PollAssetCatalog();
        }
        m_ToolActiveBinding.Update(m_ToolSystem.activeTool == m_RestrictionTool);
        m_TargetModeBinding.Update((int)Mod.SelectedTargetMode);
        m_TargetTransportBinding.Update(m_RestrictionTool.SelectedTarget != Entity.Null
            ? m_RestrictionTool.SelectedTransportMode
            : m_RestrictionTool.HoveredTransportMode);
        m_SelectedTargetKindBinding.Update(m_RestrictionTool.SelectedTarget == Entity.Null ? 0
            : EntityManager.HasComponent<Game.Net.Node>(m_RestrictionTool.SelectedTarget) ? 1 : 2);
        if (m_LastSelectedTarget != m_RestrictionTool.SelectedTarget)
        {
            m_LastSelectedTarget = m_RestrictionTool.SelectedTarget;
            m_SelectedTargetBinding.Update(m_LastSelectedTarget == Entity.Null ? string.Empty : $"{m_LastSelectedTarget.Index}:{m_LastSelectedTarget.Version}");
            LoadSelectedTargetAssets(m_LastSelectedTarget);
        }
        base.OnUpdate();
    }

    private ValueBinding<T> CreateValue<T>(string key, T value)
    {
        var binding = new ValueBinding<T>(Mod.Id, key, value, null, EqualityComparer<T>.Default);
        AddBinding(binding);
        return binding;
    }

    private void PublishRestriction() => m_RestrictionRevisionBinding.Update(++m_RestrictionRevision);

    private void ToggleTool()
    {
        m_RestrictionTool.Toggle();
    }

    public void ResetRuntimeState()
    {
        m_RestrictionTool.Deactivate();
        m_RestrictionTool.ClearSelection();
        Mod.SelectedVehicleAssets.Clear();
        Mod.SelectedTargetMode = Components.RestrictionTargetMode.Node;
        m_LastSelectedTarget = Entity.Null;
        m_SelectedTargetBinding.Update(string.Empty);
        PublishRestriction();
        m_TargetModeBinding.Update((int)Mod.SelectedTargetMode);
        m_TargetTransportBinding.Update(0);
        m_SelectedTargetKindBinding.Update(0);
        m_SelectedAssetsBinding.Update(string.Empty);
        m_ToolActiveBinding.Update(false);
        m_RestrictionTool.SetPointerOverUi(false);
    }

    public void NotifyResetCompleted() => m_ResetCompletedBinding.Update(++m_ResetCompleted);
    public void NotifyPanelClose() => m_PanelCloseBinding.Update(++m_PanelClose);

    private void SetTargetMode(int value)
    {
        Mod.SelectedTargetMode = value == 1 ? Components.RestrictionTargetMode.Segment : Components.RestrictionTargetMode.Node;
        Mod.Log.Info($"[RouteFilter.Binding] TargetMode={Mod.SelectedTargetMode}");
        m_RestrictionTool.ClearSelection();
        m_TargetModeBinding.Update((int)Mod.SelectedTargetMode);
    }

    private void RefreshAssetCatalog()
    {
        // Keep the current catalog when no vehicle prefabs are available yet so an early call
        // (for example while the world is still loading) cannot wipe a previously built list.
        if (m_VehiclePrefabQuery.IsEmptyIgnoreFilter) return;

        m_AssetsById.Clear();
        m_IdsByAsset.Clear();
        m_ModeByAsset.Clear();
        m_ChildrenByAsset.Clear();

        using var prefabEntities = m_VehiclePrefabQuery.ToEntityArray(Allocator.Temp);
        var assets = new Dictionary<Entity, AssetInfo>();
        foreach (var entity in prefabEntities)
        {
            var name = m_PrefabSystem.GetPrefabName(entity);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var info = new AssetInfo { Entity = entity, Name = name };
            if (EntityManager.TryGetComponent(entity, out CarData car))
            {
                info.Mode = 1;
                info.MaxSpeed = car.m_MaxSpeed / 2f * 3.6f;
                info.Acceleration = car.m_Acceleration;
                info.Braking = car.m_Braking;
            }
            else if (EntityManager.TryGetComponent(entity, out TrainData train))
            {
                info.Mode = 2;
                info.MaxSpeed = train.m_MaxSpeed / 2f * 3.6f;
                info.Acceleration = train.m_Acceleration;
                info.Braking = train.m_Braking;
            }
            else continue;

            if (EntityManager.TryGetComponent(entity, out CarTrailerData trailer))
            {
                info.IsTrailer = true;
                info.Parent = trailer.m_FixedTractor;
            }
            if (EntityManager.HasComponent<TrainCarriageData>(entity)) info.IsTrailer = true;
            assets[entity] = info;
        }

        foreach (var info in assets.Values)
        {
            if (EntityManager.TryGetComponent(info.Entity, out CarTractorData tractor) && tractor.m_FixedTrailer != Entity.Null)
            {
                if (assets.TryGetValue(tractor.m_FixedTrailer, out var fixedTrailer))
                {
                    fixedTrailer.Parent = info.Entity;
                    fixedTrailer.IsTrailer = true;
                }
            }

            var prefab = m_PrefabSystem.GetPrefab<VehiclePrefab>(info.Entity);
            if (prefab is not MultipleUnitTrainFrontPrefab front || front.m_Carriages == null) continue;
            foreach (var carriage in front.m_Carriages)
            {
                if (carriage?.m_Carriage == null) continue;
                var carriageEntity = m_PrefabSystem.GetEntity(carriage.m_Carriage);
                if (!assets.TryGetValue(carriageEntity, out var child)) continue;
                child.Parent = info.Entity;
                child.IsTrailer = true;
            }
        }

        var ordered = assets.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var id = i + 1;
            m_AssetsById[id] = ordered[i].Entity;
            m_IdsByAsset[ordered[i].Entity] = id;
            m_ModeByAsset[ordered[i].Entity] = ordered[i].Mode;
        }

        var lines = new List<string>(ordered.Length);
        foreach (var info in ordered)
        {
            var parentId = info.Parent != Entity.Null && m_IdsByAsset.TryGetValue(info.Parent, out var resolvedParent) ? resolvedParent : 0;
            if (parentId != 0)
            {
                if (!m_ChildrenByAsset.TryGetValue(info.Parent, out var children))
                    m_ChildrenByAsset[info.Parent] = children = new List<Entity>();
                children.Add(info.Entity);
            }
            lines.Add(string.Join("|",
                m_IdsByAsset[info.Entity], Uri.EscapeDataString(info.Name), info.Mode,
                Format(info.MaxSpeed), Format(info.Acceleration), Format(info.Braking),
                parentId, info.IsTrailer ? 1 : 0));
        }

        Mod.SelectedVehicleAssets.RemoveWhere(entity => !m_IdsByAsset.ContainsKey(entity));
        m_AssetCatalogBinding.Update(string.Join("\n", lines));
        UpdateSelectedBinding();

        // Remember the world state this catalog was built from so later prefab or
        // component additions trigger a rebuild instead of being missed forever.
        m_ContentAvailabilityDirty = false;
        m_PendingLoadRefresh = false;
        Mod.Log.Info($"Vehicle asset catalog refreshed: {ordered.Length} assets, {m_ChildrenByAsset.Sum(pair => pair.Value.Count)} grouped trailers");
    }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void LoadSelectedTargetAssets(Entity target)
    {
        Mod.SelectedVehicleAssets.Clear();
        if (target != Entity.Null && EntityManager.Exists(target) &&
            EntityManager.TryGetBuffer(target, true, out DynamicBuffer<Components.RestrictedVehicleAssetV1> assets))
        {
            foreach (var asset in assets)
                if (asset.m_Prefab != Entity.Null && m_IdsByAsset.ContainsKey(asset.m_Prefab))
                    Mod.SelectedVehicleAssets.Add(asset.m_Prefab);
        }
        UpdateSelectedBinding();
    }

    private void ToggleAsset(int id)
    {
        Mod.Log.Info($"[RouteFilter.Binding] toggleAsset received id={id}");
        if (!m_AssetsById.TryGetValue(id, out var entity)) { Mod.Log.Warn($"UI requested unknown asset id {id}"); return; }
        if (!Mod.SelectedVehicleAssets.Add(entity)) Mod.SelectedVehicleAssets.Remove(entity);
        UpdateSelectedBinding();
        Mod.Log.Debug($"Asset {id} toggled; {Mod.SelectedVehicleAssets.Count} forbidden assets selected");
    }

    private void ToggleAssetGroup(int id, bool includeChildren)
    {
        Mod.Log.Info($"[RouteFilter.Binding] toggleAssetGroup received id={id}");
        if (!m_AssetsById.TryGetValue(id, out var entity)) return;
        var group = new List<Entity> { entity };
        if (includeChildren && m_ChildrenByAsset.TryGetValue(entity, out var children)) group.AddRange(children);
        var remove = group.All(Mod.SelectedVehicleAssets.Contains);
        foreach (var item in group)
        {
            if (remove) Mod.SelectedVehicleAssets.Remove(item);
            else Mod.SelectedVehicleAssets.Add(item);
        }
        UpdateSelectedBinding();
        Mod.Log.Debug($"Asset group {id} toggled (children: {includeChildren}); {Mod.SelectedVehicleAssets.Count} forbidden assets selected");
    }

    // UI-only, user-triggered O(submitted IDs); never scans the city or expands groups.
    private void SetFilteredAssetSelection(string assetIds, bool forbidden)
    {
        if (string.IsNullOrEmpty(assetIds)) return;
        foreach (var token in assetIds.Split(','))
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || !m_AssetsById.TryGetValue(id, out var entity)) continue;
            if (forbidden) Mod.SelectedVehicleAssets.Add(entity);
            else Mod.SelectedVehicleAssets.Remove(entity);
        }
        UpdateSelectedBinding();
    }

    private void UpdateSelectedBinding()
    {
        m_SelectedAssetsBinding.Update(string.Join(",", Mod.SelectedVehicleAssets
            .Where(m_IdsByAsset.ContainsKey).Select(entity => m_IdsByAsset[entity]).OrderBy(id => id)));
    }
}
