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
    private sealed class LabelCache { internal Entity[] Assets; internal int Catalog; internal RouteFilter.Persistence.TrafficLegend[] Meanings; }
    private readonly Dictionary<Entity,LabelCache> m_LabelCache = new();
    private int m_VehicleCatalogRevision;
    private readonly Dictionary<Entity,RouteFilter.Persistence.TrafficVehicleSemantic> m_VehicleSemantics = new();
    private int m_TopologyRevision = -1;
    private float m_Scale = 1, m_Height, m_Lateral, m_Longitudinal, m_Rotation, m_PlateSpacing = .04f;
    private void LocaleChanged() { m_LoadDirty = true; }
    public void DisposeRuntimeVisuals() { ClearOwned(); m_Resources.Dispose(); Enabled = false; }
    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, Game.GameMode mode)
    {
        m_LabelCache.Clear(); m_VehicleSemantics.Clear(); ClearOwned(); m_Dirty.Clear(); m_RenderChecks.Clear();
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
        var changed = scale != m_Scale || height != m_Height || lateral != m_Lateral || spacing != m_PlateSpacing || longitudinal != m_Longitudinal || rotation != m_Rotation;
        m_Scale = scale; m_Height = height; m_Lateral = lateral; m_PlateSpacing = spacing;
        m_Longitudinal = longitudinal; m_Rotation = rotation;
        return changed;
    }
    private RouteFilter.Persistence.TrafficLegend[] Meanings(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets)) { m_LabelCache.Remove(target); return Array.Empty<RouteFilter.Persistence.TrafficLegend>(); }
        if (m_LabelCache.TryGetValue(target,out var cached) && cached.Catalog == m_VehicleCatalogRevision && cached.Assets.Length == assets.Length)
        {
            var same=true;
            for (int i=0;i<assets.Length;i++) if(cached.Assets[i]!=assets[i].m_Prefab) { same=false; break; }
            if(same) return cached.Meanings;
        }
        var selected=new List<Entity>(); foreach(var asset in assets) selected.Add(asset.m_Prefab);
        var applicable=World.GetExistingSystemManaged<RouteFilterUISystem>()?.RoadVehicleAssets ?? Enumerable.Empty<Entity>();
        var meanings=RouteFilter.Persistence.TrafficSignSemantics.Resolve(selected,applicable,VehicleCategory);
        m_LabelCache[target]=new LabelCache {Assets=selected.ToArray(),Catalog=m_VehicleCatalogRevision,Meanings=meanings};
        return meanings;
    }
    internal string[] Labels(Entity target)
    {
        var dictionary=GameManager.instance.localizationManager.activeDictionary;
        return Meanings(target).Select(value=>RouteFilter.Persistence.TrafficSignLocalization.ResolveText(value,ActiveSignLocale,key=>dictionary.TryGetValue(key,out var text)?text:null)).ToArray();
    }
    private static string ActiveSignLocale => GameManager.instance.localizationManager.activeDictionary.localeID;
    private RouteFilter.Persistence.TrafficVehicleSemantic VehicleCategory(Entity asset)
    {
        if(m_VehicleSemantics.TryGetValue(asset,out var value)) return value;
        value=ReadVehicleCategory(asset); m_VehicleSemantics[asset]=value; return value;
    }
    private RouteFilter.Persistence.TrafficVehicleSemantic ReadVehicleCategory(Entity asset)
    {
        // Conservative native components only; no vehicle-name heuristics.
        if (!EntityManager.Exists(asset) || !EntityManager.HasComponent<CarData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.SpecifiedVehicles;
        if (EntityManager.TryGetComponent(asset,out CarTrailerData trailer) && trailer.m_FixedTractor!=Entity.Null && EntityManager.Exists(trailer.m_FixedTractor)) asset=trailer.m_FixedTractor;
        if (EntityManager.TryGetComponent(asset,out MaintenanceVehicleData maintenance) && (maintenance.m_MaintenanceType & (Game.Simulation.MaintenanceType.Road | Game.Simulation.MaintenanceType.Snow))!=0) return RouteFilter.Persistence.TrafficVehicleSemantic.RoadMaintenance;
        if (EntityManager.HasComponent<PublicTransportVehicleData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.Bus;
        if (EntityManager.HasComponent<GarbageTruckData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.RefuseVehicle;
        if (EntityManager.HasComponent<FireEngineData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.FireEngine;
        if (EntityManager.HasComponent<AmbulanceData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.Ambulance;
        if (EntityManager.HasComponent<PoliceCarData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.PoliceVehicle;
        if (EntityManager.HasComponent<TaxiData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.Taxi;
        if (EntityManager.HasComponent<DeliveryTruckData>(asset) || EntityManager.HasComponent<CargoTransportVehicleData>(asset))
            return EntityManager.GetComponentData<CarData>(asset).m_SizeClass==Game.Vehicles.SizeClass.Large ? RouteFilter.Persistence.TrafficVehicleSemantic.HeavyGoodsVehicle : RouteFilter.Persistence.TrafficVehicleSemantic.GoodsVehicle;
        // Native CarData cannot reliably distinguish motorcycle / passenger car / work vehicle.
        return RouteFilter.Persistence.TrafficVehicleSemantic.SpecifiedVehicles;
    }
    private void CreateAssembly(Entity target, Entity marker, SignPrefab prefab, float3 position, quaternion rotation, RouteFilter.Persistence.TrafficLegend[] legends, string profile, float firstPlate)
    {
        var root = new GameObject("RouteFilter.Assembly:" + target) { hideFlags = HideFlags.DontSave };
        root.transform.position = position; root.transform.rotation = rotation;
        if (!m_Assemblies.TryGetValue(target,out var list)) m_Assemblies[target] = list = new();
        list.Add(root);
        var actualMainScale=1f;
        if (m_Scale != 1f)
        {
            try
            {
                var state = Game.Objects.ObjectState.Clear | Game.Objects.ObjectState.Forward;
                state |= World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic ? Game.Objects.ObjectState.LefthandTraffic : Game.Objects.ObjectState.RighthandTraffic;
                if (Enum.TryParse<Game.Objects.ObjectState>("Locale" + GameManager.instance.localizationManager.activeDictionary.localeID.Replace("-",""),true,out var locale)) state |= locale;
                m_Resources.ScaledMain(root.transform,m_PrefabSystem.GetPrefab<StaticObjectPrefab>(prefab.Entity),m_Scale,state);
                actualMainScale=m_Scale;
                EntityManager.AddComponent<Game.Tools.Hidden>(marker);
                EntityManager.AddComponent<Game.Common.BatchesUpdated>(marker);
            }
            catch (Exception error) { WarnOnce("MainScale:" + prefab.Name,error.Message + "; native main sign remains visible at scale 1"); }
        }
        // Auxiliary failures must never discard the native main sign.
        try
        {
            var bounds=EntityManager.GetComponentData<ObjectGeometryData>(prefab.Entity).m_Bounds;
            for(int i=0;i<legends.Length;i++)
                if(SignAssemblyFrame.TryPlateAnchor(bounds.min,bounds.max,actualMainScale,SignAppearance.PlateHeight(firstPlate,i,m_PlateSpacing),out var plateAnchor))
                    m_Resources.Plate(root.transform,plateAnchor,legends[i],ActiveSignLocale,profile);
            if(legends.Length>0) Mod.Log.Info($"[RouteFilter.SignFrame] target={target} main={prefab.Name} frontLocal=+Z mainBoundsFront={bounds.max.z} scale={actualMainScale} approach={-math.forward(rotation)} plateFront={bounds.max.z*actualMainScale+.020f}");
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
