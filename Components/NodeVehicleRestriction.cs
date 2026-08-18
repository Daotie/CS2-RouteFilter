using Colossal.Serialization.Entities;
using Game.Pathfind;
using Unity.Entities;

namespace NodeGate.Components;

[System.Flags]
public enum VehicleTypeMask : uint
{
    None = 0,
    PrivateCar = 1u << 0,
    Taxi = 1u << 1,
    DeliveryTruck = 1u << 2,
    GoodsDelivery = 1u << 3,
    Bus = 1u << 4,
    Tram = 1u << 5,
    PassengerTrain = 1u << 6,
    Subway = 1u << 7,
    CargoTrain = 1u << 8,
    PoliceCar = 1u << 9,
    Ambulance = 1u << 10,
    FireEngine = 1u << 11,
    GarbageTruck = 1u << 12,
    Hearse = 1u << 13,
    RoadMaintenance = 1u << 14,
    ParkMaintenance = 1u << 15,
    PostVan = 1u << 16,
    PrisonerTransport = 1u << 17,
    EvacuationTransport = 1u << 18,
    Bicycle = 1u << 19,
    All = (1u << 20) - 1
}

public struct NodeVehicleRestriction : IComponentData, ISerializable
{
    public RuleFlags m_Rules;
    public VehicleTypeMask m_VehicleTypes;

    public NodeVehicleRestriction(VehicleTypeMask vehicleTypes, RuleFlags rules)
    {
        m_VehicleTypes = vehicleTypes & VehicleTypeMask.All;
        m_Rules = rules & SupportedRules;
    }

    public static RuleFlags SupportedRules =>
        RuleFlags.ForbidCombustionEngines |
        RuleFlags.ForbidTransitTraffic |
        RuleFlags.ForbidHeavyTraffic |
        RuleFlags.ForbidPrivateTraffic;

    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
    {
        writer.Write((uint)m_VehicleTypes);
        writer.Write((byte)m_Rules);
    }

    public void Deserialize<TReader>(TReader reader) where TReader : IReader
    {
        uint vehicleTypes = 0;
        byte rules = 0;
        reader.Read(out vehicleTypes);
        reader.Read(out rules);
        m_VehicleTypes = (VehicleTypeMask)vehicleTypes & VehicleTypeMask.All;
        m_Rules = (RuleFlags)rules & SupportedRules;
    }
}
