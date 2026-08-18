using Colossal.Serialization.Entities;
using Unity.Entities;

namespace RouteFilter.Components;

public enum RestrictionTargetMode : byte
{
    Node = 0,
    Segment = 1
}

// Versioned names make the asset-level save-data contract explicit.
public struct NodeAssetRestrictionV1 : IComponentData, ISerializable
{
    public byte m_Schema;
    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write((byte)1);
    public void Deserialize<TReader>(TReader reader) where TReader : IReader
    {
        reader.Read(out m_Schema);
        m_Schema = 1;
    }
}

public struct SegmentAssetRestrictionV1 : IComponentData, ISerializable
{
    public byte m_Schema;
    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write((byte)1);
    public void Deserialize<TReader>(TReader reader) where TReader : IReader
    {
        reader.Read(out m_Schema);
        m_Schema = 1;
    }
}

[InternalBufferCapacity(8)]
public struct RestrictedVehicleAssetV1 : IBufferElementData, ISerializable
{
    public Entity m_Prefab;
    public RestrictedVehicleAssetV1(Entity prefab) => m_Prefab = prefab;
    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(m_Prefab);
    public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out m_Prefab);
}

/// <summary>A short-lived pathfinding barrier; never serialized.</summary>
public struct AccessDetourBlock : IComponentData
{
    public ushort m_RequestCount;
}

/// <summary>Tracks a vehicle while its path is recalculated; never serialized.</summary>
public struct VehicleDetourRequest : IComponentData
{
    public Entity m_Target;
    public byte m_Ticks;

    public VehicleDetourRequest(Entity target)
    {
        m_Target = target;
        m_Ticks = 0;
    }
}
