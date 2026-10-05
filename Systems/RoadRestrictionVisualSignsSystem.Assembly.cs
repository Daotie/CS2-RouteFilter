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
    private sealed class LabelCache { internal Entity[] Assets; internal int Catalog, Locale; internal string[] Labels; }
    private readonly Dictionary<Entity,LabelCache> m_LabelCache = new();
    private int m_VehicleCatalogRevision, m_LabelLocaleRevision;
    private bool m_LocaleDirty;
    private int m_TopologyRevision = -1;
    private float m_Scale = 1, m_Height, m_Lateral, m_Longitudinal, m_Rotation, m_PlateSpacing = .04f;
    private void LocaleChanged() { m_LocaleDirty = true; m_LabelLocaleRevision++; }
    public void DisposeRuntimeVisuals() { ClearOwned(); m_Resources.Dispose(); Enabled = false; }
    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, Game.GameMode mode)
    {
        m_LabelCache.Clear(); ClearOwned(); m_Dirty.Clear(); m_RenderChecks.Clear();
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
        var longitudinal = SignAppearance.Clamp(settings.RoadSignLongitudinalOffset,-10f,10f,0);
        var rotation = SignAppearance.Clamp(settings.RoadSignRotation,-180f,180f,0);
        var changed = m_LocaleDirty || scale != m_Scale || height != m_Height || lateral != m_Lateral || spacing != m_PlateSpacing || longitudinal != m_Longitudinal || rotation != m_Rotation;
        m_LocaleDirty = false; m_Scale = scale; m_Height = height; m_Lateral = lateral; m_PlateSpacing = spacing;
        m_Longitudinal = longitudinal; m_Rotation = rotation;
        return changed;
    }
    internal string[] Labels(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets)) { m_LabelCache.Remove(target); return Array.Empty<string>(); }
        if (m_LabelCache.TryGetValue(target,out var cached) && cached.Catalog == m_VehicleCatalogRevision && cached.Locale == m_LabelLocaleRevision && cached.Assets.Length == assets.Length)
        {
            var same = true;
            for (int i=0;i<assets.Length;i++) if (cached.Assets[i] != assets[i].m_Prefab) { same = false; break; }
            if (same) return cached.Labels;
        }
        var selected = new List<Entity>(); foreach (var asset in assets) selected.Add(asset.m_Prefab);
        var applicable = World.GetExistingSystemManaged<RouteFilterUISystem>()?.RoadVehicleAssets ?? Enumerable.Empty<Entity>();
        var categories = RouteFilter.Persistence.VehiclePlateLabels.Select(selected,applicable,VehicleCategory);
        var dictionary = GameManager.instance.localizationManager.activeDictionary;
        var labels = categories.Select(category => dictionary.TryGetValue("RouteFilter.Plate."+category,out var text) ? text : category).ToArray();
        m_LabelCache[target] = new LabelCache {Assets=selected.ToArray(),Catalog=m_VehicleCatalogRevision,Locale=m_LabelLocaleRevision,Labels=labels};
        return labels;
    }
    private string VehicleCategory(Entity asset)
    {
        if (!EntityManager.HasComponent<CarData>(asset)) return null;
        if (EntityManager.TryGetComponent(asset,out CarTrailerData trailer) && trailer.m_FixedTractor != Entity.Null && EntityManager.Exists(trailer.m_FixedTractor)) asset = trailer.m_FixedTractor;
        if (EntityManager.HasComponent<PublicTransportVehicleData>(asset)) return "Buses";
        if (EntityManager.HasComponent<GarbageTruckData>(asset)) return "Garbage";
        if (EntityManager.HasComponent<FireEngineData>(asset)) return "Fire";
        if (EntityManager.HasComponent<AmbulanceData>(asset)) return "Ambulance";
        if (EntityManager.HasComponent<PoliceCarData>(asset)) return "Police";
        if (EntityManager.HasComponent<TaxiData>(asset)) return "Taxi";
        if (EntityManager.HasComponent<DeliveryTruckData>(asset) || EntityManager.HasComponent<CargoTransportVehicleData>(asset)) return "Trucks";
        var name = m_PrefabSystem.GetPrefabName(asset) ?? string.Empty;
        if (name.IndexOf("motorcycle",StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("motorbike",StringComparison.OrdinalIgnoreCase)>=0) return "Motorcycles";
        if (name.IndexOf("truck",StringComparison.OrdinalIgnoreCase)>=0) return "Trucks";
        return "Cars";
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
