using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using NodeGate.Components;
using Unity.Collections;
using Unity.Entities;

namespace NodeGate.Systems;

/// <summary>
/// Exact-type enforcement layer. Vanilla RuleFlags cannot represent individual
/// service vehicles, so the beta stops matching vehicles before a restricted node.
/// </summary>
public sealed partial class VehicleGateSystem : GameSystemBase
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
        if (!Mod.Settings.EnableExactGate) return;
        GateCars();
        GateTrains();
    }

    private void GateCars()
    {
        using var vehicles = m_Cars.ToEntityArray(Allocator.Temp);
        foreach (var vehicle in vehicles)
        {
            var type = Classify(vehicle);
            if (type == VehicleTypeMask.None || IsProtectedEmergency(type) || !IsBlocked(vehicle, type, true)) continue;
            var navigation = EntityManager.GetComponentData<CarNavigation>(vehicle);
            var moving = EntityManager.GetComponentData<Moving>(vehicle);
            navigation.m_MaxSpeed = 0f;
            moving.m_Velocity = default;
            moving.m_AngularVelocity = default;
            EntityManager.SetComponentData(vehicle, navigation);
            EntityManager.SetComponentData(vehicle, moving);
        }
    }

    private void GateTrains()
    {
        using var vehicles = m_Trains.ToEntityArray(Allocator.Temp);
        foreach (var vehicle in vehicles)
        {
            var type = Classify(vehicle);
            if (type == VehicleTypeMask.None || !IsBlocked(vehicle, type, false)) continue;
            var navigation = EntityManager.GetComponentData<TrainNavigation>(vehicle);
            var moving = EntityManager.GetComponentData<Moving>(vehicle);
            navigation.m_Speed = 0f;
            moving.m_Velocity = default;
            moving.m_AngularVelocity = default;
            EntityManager.SetComponentData(vehicle, navigation);
            EntityManager.SetComponentData(vehicle, moving);
        }
    }

    private bool IsBlocked(Entity vehicle, VehicleTypeMask type, bool car)
    {
        var count = 0;
        var limit = System.Math.Max(1, Mod.Settings.LookAheadLanes);
        if (car)
        {
            var lanes = EntityManager.GetBuffer<CarNavigationLane>(vehicle, true);
            for (var i = 0; i < lanes.Length && count++ < limit; i++)
                if (LaneBlocks(lanes[i].m_Lane, type)) return true;
        }
        else
        {
            var lanes = EntityManager.GetBuffer<TrainNavigationLane>(vehicle, true);
            for (var i = 0; i < lanes.Length && count++ < limit; i++)
                if (LaneBlocks(lanes[i].m_Lane, type)) return true;
        }
        return false;
    }

    private bool LaneBlocks(Entity lane, VehicleTypeMask type)
    {
        var entity = lane;
        for (var depth = 0; depth < 3 && entity != Entity.Null; depth++)
        {
            if (EntityManager.TryGetComponent(entity, out NodeVehicleRestriction restriction))
                return (restriction.m_VehicleTypes & type) != 0;
            if (!EntityManager.TryGetComponent(entity, out Owner owner)) break;
            entity = owner.m_Owner;
        }
        return false;
    }

    private bool IsProtectedEmergency(VehicleTypeMask type) => Mod.Settings.ProtectEmergencyVehicles &&
        (type & (VehicleTypeMask.PoliceCar | VehicleTypeMask.Ambulance | VehicleTypeMask.FireEngine)) != 0;

    private VehicleTypeMask Classify(Entity entity)
    {
        if (EntityManager.HasComponent<Game.Vehicles.PoliceCar>(entity)) return VehicleTypeMask.PoliceCar;
        if (EntityManager.HasComponent<Game.Vehicles.Ambulance>(entity)) return VehicleTypeMask.Ambulance;
        if (EntityManager.HasComponent<Game.Vehicles.FireEngine>(entity)) return VehicleTypeMask.FireEngine;
        if (EntityManager.HasComponent<Game.Vehicles.GarbageTruck>(entity)) return VehicleTypeMask.GarbageTruck;
        if (EntityManager.HasComponent<Game.Vehicles.Hearse>(entity)) return VehicleTypeMask.Hearse;
        if (EntityManager.HasComponent<RoadMaintenanceVehicle>(entity)) return VehicleTypeMask.RoadMaintenance;
        if (EntityManager.HasComponent<ParkMaintenanceVehicle>(entity)) return VehicleTypeMask.ParkMaintenance;
        if (EntityManager.HasComponent<Game.Vehicles.PostVan>(entity)) return VehicleTypeMask.PostVan;
        if (EntityManager.HasComponent<PrisonerTransport>(entity)) return VehicleTypeMask.PrisonerTransport;
        if (EntityManager.HasComponent<EvacuatingTransport>(entity)) return VehicleTypeMask.EvacuationTransport;
        if (EntityManager.HasComponent<Game.Vehicles.Taxi>(entity)) return VehicleTypeMask.Taxi;
        if (EntityManager.HasComponent<Game.Vehicles.DeliveryTruck>(entity)) return VehicleTypeMask.DeliveryTruck;
        if (EntityManager.HasComponent<GoodsDeliveryVehicle>(entity)) return VehicleTypeMask.GoodsDelivery;
        if (EntityManager.HasComponent<Bicycle>(entity)) return VehicleTypeMask.Bicycle;
        if (EntityManager.HasComponent<Train>(entity)) return ClassifyTrain(entity);
        if (EntityManager.HasComponent<Game.Vehicles.PublicTransport>(entity)) return VehicleTypeMask.Bus;
        if (EntityManager.HasComponent<Game.Vehicles.PersonalCar>(entity)) return VehicleTypeMask.PrivateCar;
        return VehicleTypeMask.None;
    }

    private VehicleTypeMask ClassifyTrain(Entity entity)
    {
        if (EntityManager.HasComponent<Game.Vehicles.CargoTransport>(entity)) return VehicleTypeMask.CargoTrain;
        if (!EntityManager.TryGetComponent(entity, out PrefabRef prefabRef) ||
            !EntityManager.TryGetComponent(prefabRef.m_Prefab, out TrainData trainData))
            return VehicleTypeMask.PassengerTrain;
        if ((trainData.m_TrackType & TrackTypes.Tram) != 0) return VehicleTypeMask.Tram;
        if ((trainData.m_TrackType & TrackTypes.Subway) != 0) return VehicleTypeMask.Subway;
        return VehicleTypeMask.PassengerTrain;
    }
}
