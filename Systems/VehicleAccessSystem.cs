using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using NetSubLane = Game.Net.SubLane;

namespace RouteFilter.Systems;

/// <summary>
/// Asset-level enforcement layer. The vehicle's exact PrefabRef is compared with
/// the saved asset list before an alternate path is requested.
/// </summary>
public sealed partial class VehicleAccessSystem : GameSystemBase
{
    private EntityQuery m_Cars;
    private EntityQuery m_Trains;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Cars = GetEntityQuery(
            ComponentType.ReadWrite<CarNavigation>(), ComponentType.ReadWrite<Moving>(),
            ComponentType.ReadOnly<CarNavigationLane>(), ComponentType.Exclude<Deleted>());
        m_Trains = GetEntityQuery(
            ComponentType.ReadWrite<TrainNavigation>(), ComponentType.ReadWrite<Moving>(),
            ComponentType.ReadOnly<TrainNavigationLane>(), ComponentType.Exclude<Deleted>());
    }

    protected override void OnUpdate()
    {
        if (!Mod.Settings.EnableExactRestrictions) return;
        ProcessCars();
        ProcessTrains();
    }

    private void ProcessCars()
    {
        using var vehicles = m_Cars.ToEntityArray(Allocator.Temp);
        foreach (var vehicle in vehicles)
        {
            if (IsProtectedEmergency(vehicle) || !TryGetVehiclePrefab(vehicle, out var prefab) ||
                !TryGetBlockedTarget(vehicle, prefab, true, out var target)) continue;
            RequestDetour(vehicle, target);
            var navigation = EntityManager.GetComponentData<CarNavigation>(vehicle);
            var moving = EntityManager.GetComponentData<Moving>(vehicle);
            navigation.m_MaxSpeed = 0f;
            moving.m_Velocity = default;
            moving.m_AngularVelocity = default;
            EntityManager.SetComponentData(vehicle, navigation);
            EntityManager.SetComponentData(vehicle, moving);
        }
    }

    private void ProcessTrains()
    {
        using var vehicles = m_Trains.ToEntityArray(Allocator.Temp);
        foreach (var vehicle in vehicles)
        {
            if (!TryGetVehiclePrefab(vehicle, out var prefab) ||
                !TryGetBlockedTarget(vehicle, prefab, false, out var target)) continue;
            RequestDetour(vehicle, target);
            var navigation = EntityManager.GetComponentData<TrainNavigation>(vehicle);
            var moving = EntityManager.GetComponentData<Moving>(vehicle);
            navigation.m_Speed = 0f;
            moving.m_Velocity = default;
            moving.m_AngularVelocity = default;
            EntityManager.SetComponentData(vehicle, navigation);
            EntityManager.SetComponentData(vehicle, moving);
        }
    }

    private bool TryGetBlockedTarget(Entity vehicle, Entity prefab, bool car, out Entity blockedTarget)
    {
        blockedTarget = Entity.Null;
        var count = 0;
        var limit = System.Math.Max(1, Mod.Settings.LookAheadLanes);
        if (car)
        {
            var lanes = EntityManager.GetBuffer<CarNavigationLane>(vehicle, true);
            for (var i = 0; i < lanes.Length && count++ < limit; i++)
                if (TryGetBlockingTarget(lanes[i].m_Lane, prefab, out blockedTarget)) return true;
        }
        else
        {
            var lanes = EntityManager.GetBuffer<TrainNavigationLane>(vehicle, true);
            for (var i = 0; i < lanes.Length && count++ < limit; i++)
                if (TryGetBlockingTarget(lanes[i].m_Lane, prefab, out blockedTarget)) return true;
        }
        return false;
    }

    private bool TryGetBlockingTarget(Entity lane, Entity prefab, out Entity blockedTarget)
    {
        blockedTarget = Entity.Null;
        var entity = lane;
        for (var depth = 0; depth < 3 && entity != Entity.Null; depth++)
        {
            if (EntityManager.HasComponent<NodeAssetRestrictionV1>(entity) && IsAssetRestricted(entity, prefab))
            {
                blockedTarget = entity;
                return true;
            }
            if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(entity) && IsAssetRestricted(entity, prefab))
            {
                blockedTarget = entity;
                return true;
            }
            if (!EntityManager.TryGetComponent(entity, out Owner owner)) break;
            entity = owner.m_Owner;
        }
        return false;
    }

    private bool IsAssetRestricted(Entity target, Entity prefab)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets)) return false;
        foreach (var asset in assets)
            if (asset.m_Prefab == prefab) return true;
        return false;
    }

    private bool TryGetVehiclePrefab(Entity vehicle, out Entity prefab)
    {
        prefab = Entity.Null;
        if (!EntityManager.TryGetComponent(vehicle, out PrefabRef prefabRef)) return false;
        prefab = prefabRef.m_Prefab;
        return prefab != Entity.Null;
    }

    private void RequestDetour(Entity vehicle, Entity target)
    {
        if (EntityManager.HasComponent<VehicleDetourRequest>(vehicle)) return;

        EntityManager.AddComponentData(vehicle, new VehicleDetourRequest(target));
        if (EntityManager.TryGetComponent(target, out AccessDetourBlock block))
        {
            if (block.m_RequestCount < ushort.MaxValue) block.m_RequestCount++;
            EntityManager.SetComponentData(target, block);
        }
        else
        {
            EntityManager.AddComponentData(target, new AccessDetourBlock { m_RequestCount = 1 });
            MarkTargetLanesUpdated(target);
        }
    }

    private void MarkTargetLanesUpdated(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<NetSubLane> lanes)) return;
        foreach (var subLane in lanes)
            if (!EntityManager.HasComponent<Updated>(subLane.m_SubLane))
                EntityManager.AddComponent<Updated>(subLane.m_SubLane);
    }

    private bool IsProtectedEmergency(Entity entity) => Mod.Settings.ProtectEmergencyVehicles &&
        (EntityManager.HasComponent<Game.Vehicles.PoliceCar>(entity) ||
         EntityManager.HasComponent<Game.Vehicles.Ambulance>(entity) ||
         EntityManager.HasComponent<Game.Vehicles.FireEngine>(entity));
}
