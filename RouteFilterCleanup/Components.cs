using Colossal.Serialization.Entities;
using Unity.Entities;

// These names intentionally match the historical RouteFilter component identities.
// The cleanup mod contains data definitions only; it does not contain old enforcement systems.
namespace RouteFilter.Components
{

public struct NodeAssetRestrictionV1 : IComponentData, ISerializable
{
    public byte m_Schema;
    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write((byte)1);
    public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out m_Schema); m_Schema = 1; }
}

public struct SegmentAssetRestrictionV1 : IComponentData, ISerializable
{
    public byte m_Schema;
    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write((byte)1);
    public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out m_Schema); m_Schema = 1; }
}

[InternalBufferCapacity(8)]
public struct RestrictedVehicleAssetV1 : IBufferElementData, ISerializable
{
    public Entity m_Prefab;
    public RestrictedVehicleAssetV1(Entity prefab) => m_Prefab = prefab;
    public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(m_Prefab);
    public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out m_Prefab);
}

public struct AccessDetourBlock : IComponentData
{
    public ushort m_RequestCount;
    public byte m_QuietTicks;
    public uint m_AcquiredFrame;
}

public struct VehicleDetourRequest : IComponentData
{
    public Entity m_Target;
    public ushort m_Ticks;
    public byte m_Attempts;
    public ushort m_WaitTicks;
    public bool m_RetryPending;
    public bool m_Invalidated;
    public bool m_MustStop;
}

public struct RerouteCooldown : IComponentData
{
    public byte m_Ticks;
    public RerouteCooldown(byte ticks) => m_Ticks = ticks;
}
}
