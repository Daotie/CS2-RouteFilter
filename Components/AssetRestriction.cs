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

// Legacy 1.x runtime components (AccessDetourBlock, VehicleDetourRequest, RerouteCooldown) were
// retired with the 1.x enforcement systems. They were never ISerializable, so a city saved by
// 1.x never carried them and no migration or compatibility code is required for them. Their only
// remaining reason to exist would be a cleanup pass, and that belongs to the standalone
// RouteFilterCleanup mod, not to RouteFilter itself: a startup full-city scan on every load would
// reintroduce the performance, safety and ownership risks the 2.0 rewrite exists to remove.
