using Colossal.Entities;
using Game.Prefabs;
using Unity.Entities;
namespace RouteFilter.Systems;
// Shared display-only metadata. Never consulted by restriction matching/enforcement.
internal static class VehicleSemanticClassifier
{
    internal static RouteFilter.Persistence.TrafficVehicleSemantic Classify(EntityManager manager, Entity asset)
    {
        // Conservative native components only; no vehicle-name heuristics.
        if (!manager.Exists(asset) || !manager.HasComponent<CarData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.SpecifiedVehicles;
        if (manager.TryGetComponent(asset,out CarTrailerData trailer) && trailer.m_FixedTractor!=Entity.Null && manager.Exists(trailer.m_FixedTractor)) asset=trailer.m_FixedTractor;
        if (manager.TryGetComponent(asset,out MaintenanceVehicleData maintenance) && (maintenance.m_MaintenanceType & (Game.Simulation.MaintenanceType.Road | Game.Simulation.MaintenanceType.Snow))!=0) return RouteFilter.Persistence.TrafficVehicleSemantic.RoadMaintenance;
        if (manager.HasComponent<PublicTransportVehicleData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.Bus;
        if (manager.HasComponent<GarbageTruckData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.RefuseVehicle;
        if (manager.HasComponent<FireEngineData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.FireEngine;
        if (manager.HasComponent<AmbulanceData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.Ambulance;
        if (manager.HasComponent<PoliceCarData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.PoliceVehicle;
        if (manager.HasComponent<TaxiData>(asset)) return RouteFilter.Persistence.TrafficVehicleSemantic.Taxi;
        if (manager.HasComponent<DeliveryTruckData>(asset) || manager.HasComponent<CargoTransportVehicleData>(asset))
            return manager.GetComponentData<CarData>(asset).m_SizeClass==Game.Vehicles.SizeClass.Large ? RouteFilter.Persistence.TrafficVehicleSemantic.HeavyGoodsVehicle : RouteFilter.Persistence.TrafficVehicleSemantic.GoodsVehicle;
        // Exact remainder of the UI road category partition; not an invented passenger-car taxonomy.
        // Missing/non-road assets still return SpecifiedVehicles above.
        return RouteFilter.Persistence.TrafficVehicleSemantic.OtherRoadVehicles;

    }
    internal static string Category(EntityManager manager, Entity asset, int mode)
    {
        if (mode == 2) return "Rail";
        return Classify(manager, asset) switch
        {
            RouteFilter.Persistence.TrafficVehicleSemantic.Bus => "Bus",
            RouteFilter.Persistence.TrafficVehicleSemantic.Taxi => "Taxi",
            RouteFilter.Persistence.TrafficVehicleSemantic.GoodsVehicle => "Goods",
            RouteFilter.Persistence.TrafficVehicleSemantic.HeavyGoodsVehicle => "Goods",
            RouteFilter.Persistence.TrafficVehicleSemantic.FireEngine => "Emergency",
            RouteFilter.Persistence.TrafficVehicleSemantic.Ambulance => "Emergency",
            RouteFilter.Persistence.TrafficVehicleSemantic.PoliceVehicle => "Emergency",
            RouteFilter.Persistence.TrafficVehicleSemantic.RoadMaintenance => "Service",
            RouteFilter.Persistence.TrafficVehicleSemantic.RefuseVehicle => "Service",
            _ => "Other"
        };
    }
}
