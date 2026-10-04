using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Game.Prefabs;
using Game.SceneFlow;
using RouteFilter.Components;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RouteFilter.Systems;

public sealed partial class RoadRestrictionVisualSignsSystem
{
    private readonly Dictionary<Entity, List<GameObject>> m_Assemblies = new();
    private RuntimeSignResources m_Resources = new();
    private bool m_LocaleDirty;
    private int m_TopologyRevision = -1;
    private float m_Scale = 1, m_Height, m_Lateral, m_PlateSpacing = .04f;
    private void LocaleChanged() => m_LocaleDirty = true;
    public void DisposeRuntimeVisuals() { ClearOwned(); m_Resources.Dispose(); Enabled = false; }
    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, Game.GameMode mode)
    {
        ClearOwned(); m_Dirty.Clear(); m_RenderChecks.Clear();
        m_Resources.Dispose(); m_Resources = new(); m_TopologyRevision = -1; m_LoadDirty = true;
        base.OnGamePreload(purpose, mode);
    }
    private bool RefreshAppearance()
    {
        var settings = Mod.Settings;
        var scale = SignAppearance.Clamp(settings.RoadSignScale,.5f,2f,1f);
        var height = SignAppearance.Clamp(settings.RoadSignHeight,0f,5f,0f);
        var lateral = SignAppearance.Clamp(settings.RoadSignLateralOffset,-.5f,3f,0f);
        var spacing = SignAppearance.Clamp(settings.RoadPlateSpacing,.02f,.2f,.04f);
        var changed = m_LocaleDirty || scale != m_Scale || height != m_Height || lateral != m_Lateral || spacing != m_PlateSpacing;
        m_LocaleDirty = false; m_Scale = scale; m_Height = height; m_Lateral = lateral; m_PlateSpacing = spacing;
        return changed;
    }
    private string[] Labels(Entity target)
    {
        var categories = new SortedSet<string>(StringComparer.Ordinal);
        if (!EntityManager.TryGetBuffer(target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets)) return Array.Empty<string>();
        foreach (var asset in assets)
        {
            if (!EntityManager.HasComponent<CarData>(asset.m_Prefab)) continue;
            string category = "Cars";
            if (EntityManager.HasComponent<PublicTransportVehicleData>(asset.m_Prefab)) category = "Buses";
            else if (EntityManager.HasComponent<GarbageTruckData>(asset.m_Prefab)) category = "Garbage";
            else if (EntityManager.HasComponent<FireEngineData>(asset.m_Prefab)) category = "Fire";
            else if (EntityManager.HasComponent<AmbulanceData>(asset.m_Prefab)) category = "Ambulance";
            else if (EntityManager.HasComponent<PoliceCarData>(asset.m_Prefab)) category = "Police";
            else if (EntityManager.HasComponent<TaxiData>(asset.m_Prefab)) category = "Taxi";
            else if (EntityManager.HasComponent<DeliveryTruckData>(asset.m_Prefab)) category = "Trucks";
            categories.Add(category);
        }
        var dictionary = GameManager.instance.localizationManager.activeDictionary;
        return categories.Select(category => dictionary.TryGetValue("RouteFilter.Plate." + category,out var text) ? text : category).ToArray();
    }
    private void CreateAssembly(Entity target, Entity marker, SignPrefab prefab, float3 position, quaternion rotation, string[] labels, float firstPlate)
    {
        var root = new GameObject("RouteFilter.Assembly:" + target) { hideFlags = HideFlags.DontSave };
        root.transform.position = position; root.transform.rotation = rotation;
        if (!m_Assemblies.TryGetValue(target,out var list)) m_Assemblies[target] = list = new();
        list.Add(root);
        if (m_Scale != 1f)
        {
            try
            {
                var state = Game.Objects.ObjectState.Clear | Game.Objects.ObjectState.Forward;
                state |= World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic ? Game.Objects.ObjectState.LefthandTraffic : Game.Objects.ObjectState.RighthandTraffic;
                if (Enum.TryParse<Game.Objects.ObjectState>("Locale" + GameManager.instance.localizationManager.activeDictionary.localeID.Replace("-",""),true,out var locale)) state |= locale;
                m_Resources.ScaledMain(root.transform,m_PrefabSystem.GetPrefab<StaticObjectPrefab>(prefab.Entity),m_Scale,state);
                EntityManager.AddComponent<Game.Tools.Hidden>(marker);
                EntityManager.AddComponent<Game.Common.BatchesUpdated>(marker);
            }
            catch (Exception error) { WarnOnce("MainScale:" + prefab.Name,error.Message + "; native main sign remains visible at scale 1"); }
        }
        // Auxiliary failures must never discard the native main sign.
        try
        {
            for (int i = 0; i < labels.Length; i++) m_Resources.Plate(root.transform, SignAppearance.PlateHeight(firstPlate,i,m_PlateSpacing),labels[i]);
        }
        catch (Exception error) { WarnOnce("RF-Plate", "RF-Plate unavailable; main sign retained: " + error.Message); }
    }
    private void RemoveAssembly(Entity target)
    {
        if (!m_Assemblies.TryGetValue(target,out var roots)) return;
        foreach (var root in roots) Object.Destroy(root);
        m_Assemblies.Remove(target);
    }
}
