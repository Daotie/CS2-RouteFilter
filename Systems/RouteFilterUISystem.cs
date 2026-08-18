using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using RouteFilter.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RouteFilterUISystem : UISystemBase
{
    private ToolSystem m_ToolSystem = null!;
    private RestrictionToolSystem m_RestrictionTool = null!;
    private PrefabSystem m_PrefabSystem = null!;
    private EntityQuery m_VehiclePrefabQuery;
    private readonly Dictionary<int, Entity> m_AssetsById = new();
    private readonly Dictionary<Entity, int> m_IdsByAsset = new();
    private string m_AssetCatalog = string.Empty;

    public override GameMode gameMode => GameMode.GameOrEditor;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        m_RestrictionTool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_VehiclePrefabQuery = GetEntityQuery(ComponentType.ReadOnly<VehicleData>(), ComponentType.ReadOnly<PrefabData>());
        AddUpdateBinding(new GetterValueBinding<bool>(Mod.Id, "toolActive", () => m_ToolSystem.activeTool == m_RestrictionTool));
        AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "targetMode", () => (int)Mod.SelectedTargetMode));
        AddUpdateBinding(new GetterValueBinding<string>(Mod.Id, "assetCatalog", GetAssetCatalog));
        AddUpdateBinding(new GetterValueBinding<string>(Mod.Id, "selectedAssetIds", GetSelectedAssetIds));
        AddBinding(new TriggerBinding(Mod.Id, "toggleTool", m_RestrictionTool.Toggle));
        AddBinding(new TriggerBinding<int>(Mod.Id, "toggleAsset", ToggleAsset));
        AddBinding(new TriggerBinding<int>(Mod.Id, "setTargetMode", value => Mod.SelectedTargetMode = value == 1 ? RestrictionTargetMode.Segment : RestrictionTargetMode.Node));
        AddBinding(new TriggerBinding(Mod.Id, "selectAllAssets", SelectAllAssets));
        AddBinding(new TriggerBinding(Mod.Id, "selectNoAssets", Mod.SelectedVehicleAssets.Clear));
        AddBinding(new TriggerBinding(Mod.Id, "refreshAssets", RefreshAssetCatalog));
    }

    private string GetAssetCatalog()
    {
        if (m_AssetCatalog.Length == 0) RefreshAssetCatalog();
        return m_AssetCatalog;
    }

    private void RefreshAssetCatalog()
    {
        m_AssetsById.Clear();
        m_IdsByAsset.Clear();

        using var prefabEntities = m_VehiclePrefabQuery.ToEntityArray(Allocator.Temp);
        var assets = prefabEntities
            .Select(entity => (Entity: entity, Name: m_PrefabSystem.GetPrefabName(entity)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lines = new List<string>(assets.Length);
        for (var i = 0; i < assets.Length; i++)
        {
            var id = i + 1;
            m_AssetsById[id] = assets[i].Entity;
            m_IdsByAsset[assets[i].Entity] = id;
            lines.Add($"{id}|{Uri.EscapeDataString(assets[i].Name)}");
        }

        Mod.SelectedVehicleAssets.RemoveWhere(entity => !m_IdsByAsset.ContainsKey(entity));
        m_AssetCatalog = string.Join("\n", lines);
        Mod.Log.Info($"Vehicle asset catalog refreshed: {assets.Length} assets");
    }

    private string GetSelectedAssetIds()
    {
        if (m_AssetCatalog.Length == 0) RefreshAssetCatalog();
        return string.Join(",", Mod.SelectedVehicleAssets
            .Where(m_IdsByAsset.ContainsKey)
            .Select(entity => m_IdsByAsset[entity])
            .OrderBy(id => id));
    }

    private void ToggleAsset(int id)
    {
        if (!m_AssetsById.TryGetValue(id, out var entity)) return;
        if (!Mod.SelectedVehicleAssets.Add(entity)) Mod.SelectedVehicleAssets.Remove(entity);
    }

    private void SelectAllAssets()
    {
        if (m_AssetCatalog.Length == 0) RefreshAssetCatalog();
        Mod.SelectedVehicleAssets.Clear();
        foreach (var entity in m_AssetsById.Values) Mod.SelectedVehicleAssets.Add(entity);
    }
}
