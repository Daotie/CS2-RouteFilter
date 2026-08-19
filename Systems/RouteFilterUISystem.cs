using Colossal.UI.Binding;
using Colossal.Entities;
using Game;
using Game.Prefabs;
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

        AddBinding(new TriggerBinding(Mod.Id, "toggleTool", ToggleTool));
        AddBinding(new TriggerBinding<int>(Mod.Id, "toggleAsset", ToggleAsset));
        AddBinding(new TriggerBinding<int, bool>(Mod.Id, "toggleAssetGroup", ToggleAssetGroup));
        AddBinding(new TriggerBinding<int>(Mod.Id, "setTargetMode", SetTargetMode));
        AddBinding(new TriggerBinding<int>(Mod.Id, "selectAllAssets", SelectAllAssets));
        AddBinding(new TriggerBinding<int>(Mod.Id, "selectNoAssets", SelectNoAssets));
        AddBinding(new TriggerBinding(Mod.Id, "refreshAssets", RefreshAssetCatalog));
        AddBinding(new TriggerBinding(Mod.Id, "applySelection", m_RestrictionTool.ApplySelection));
        AddBinding(new TriggerBinding(Mod.Id, "clearSelectedRestriction", m_RestrictionTool.ClearSelectedRestriction));
        AddBinding(new TriggerBinding(Mod.Id, "cancelSelection", m_RestrictionTool.ClearSelection));
        AddBinding(new TriggerBinding<bool>(Mod.Id, "setPointerOverUi", m_RestrictionTool.SetPointerOverUi));
    }

    protected override void OnUpdate()
    {
        if (m_AssetsById.Count == 0 && !m_VehiclePrefabQuery.IsEmptyIgnoreFilter) RefreshAssetCatalog();
        m_ToolActiveBinding.Update(m_ToolSystem.activeTool == m_RestrictionTool);
        m_TargetModeBinding.Update((int)Mod.SelectedTargetMode);
        m_TargetTransportBinding.Update(m_RestrictionTool.SelectedTarget != Entity.Null
            ? m_RestrictionTool.SelectedTransportMode
            : m_RestrictionTool.HoveredTransportMode);
        m_SelectedTargetKindBinding.Update(m_RestrictionTool.SelectedTarget == Entity.Null ? 0
            : EntityManager.HasComponent<Game.Net.Node>(m_RestrictionTool.SelectedTarget) ? 1 : 2);
        base.OnUpdate();
    }

    private ValueBinding<T> CreateValue<T>(string key, T value)
    {
        var binding = new ValueBinding<T>(Mod.Id, key, value, null, EqualityComparer<T>.Default);
        AddBinding(binding);
        return binding;
    }

    private void ToggleTool()
    {
        if (m_AssetsById.Count == 0) RefreshAssetCatalog();
        m_RestrictionTool.Toggle();
    }

    private void SetTargetMode(int value)
    {
        Mod.SelectedTargetMode = value == 1 ? Components.RestrictionTargetMode.Segment : Components.RestrictionTargetMode.Node;
        m_RestrictionTool.ClearSelection();
        m_TargetModeBinding.Update((int)Mod.SelectedTargetMode);
    }

    private void RefreshAssetCatalog()
    {
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
                info.MaxSpeed = car.m_MaxSpeed / 2f;
                info.Acceleration = car.m_Acceleration;
                info.Braking = car.m_Braking;
            }
            else if (EntityManager.TryGetComponent(entity, out TrainData train))
            {
                info.Mode = 2;
                info.MaxSpeed = train.m_MaxSpeed / 2f;
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
        Mod.Log.Info($"Vehicle asset catalog refreshed once: {ordered.Length} assets, {m_ChildrenByAsset.Sum(pair => pair.Value.Count)} grouped trailers");
    }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void ToggleAsset(int id)
    {
        if (!m_AssetsById.TryGetValue(id, out var entity)) { Mod.Log.Warn($"UI requested unknown asset id {id}"); return; }
        if (!Mod.SelectedVehicleAssets.Add(entity)) Mod.SelectedVehicleAssets.Remove(entity);
        UpdateSelectedBinding();
        Mod.Log.Debug($"Asset {id} toggled; {Mod.SelectedVehicleAssets.Count} forbidden assets selected");
    }

    private void ToggleAssetGroup(int id, bool includeChildren)
    {
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

    private void SelectAllAssets(int mode)
    {
        foreach (var pair in m_AssetsById)
            if (mode == 0 || m_ModeByAsset[pair.Value] == mode) Mod.SelectedVehicleAssets.Add(pair.Value);
        UpdateSelectedBinding();
        Mod.Log.Debug($"All {m_AssetsById.Count} catalog assets selected as forbidden");
    }

    private void SelectNoAssets(int mode)
    {
        Mod.SelectedVehicleAssets.RemoveWhere(entity => mode == 0 || (m_ModeByAsset.TryGetValue(entity, out var assetMode) && assetMode == mode));
        UpdateSelectedBinding();
        Mod.Log.Debug("All catalog assets set to allowed");
    }

    private void UpdateSelectedBinding()
    {
        m_SelectedAssetsBinding.Update(string.Join(",", Mod.SelectedVehicleAssets
            .Where(m_IdsByAsset.ContainsKey).Select(entity => m_IdsByAsset[entity]).OrderBy(id => id)));
    }
}
