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
    /// <summary>
    /// Ticks since the last request released while no new request arrived. The barrier is
    /// removed only after this quiet grace elapses, so consecutive matching vehicles reuse
    /// one pathfind update instead of each triggering a full network recompute.
    /// </summary>
    public byte m_QuietTicks;
    /// <summary>
    /// Simulation frame at which the barrier was acquired. Drives the hard expiry: no
    /// barrier may live longer than the hard cap, regardless of request accounting.
    /// </summary>
    public uint m_AcquiredFrame;
}

/// <summary>Tracks a vehicle while its path is recalculated; never serialized.</summary>
public struct VehicleDetourRequest : IComponentData
{
    public Entity m_Target;
    /// <summary>Total ticks since the request was created; also drives the rail failsafe timer.</summary>
    public ushort m_Ticks;
    /// <summary>Rail reroute attempts, for exponential backoff.</summary>
    public byte m_Attempts;
    /// <summary>Ticks until the next rail retry; zero while actively rerouting.</summary>
    public ushort m_WaitTicks;
    /// <summary>True while a rail retry waits for the recomputed path to become ready.</summary>
    public bool m_RetryPending;
    /// <summary>True once the path invalidation (Obsolete + Updated) was actually submitted.</summary>
    public bool m_Invalidated;
    /// <summary>
    /// True when a rail vehicle must be held stopped while waiting. Road vehicles never
    /// set this: RouteFilter does not write any motion state for road vehicles; vanilla
    /// owns road vehicle movement and stopping.
    /// </summary>
    public bool m_MustStop;

    public VehicleDetourRequest(Entity target)
    {
        m_Target = target;
        m_Ticks = 0;
        m_Attempts = 0;
        m_WaitTicks = 0;
        m_RetryPending = false;
        m_Invalidated = false;
        m_MustStop = false;
    }
}

/// <summary>
/// Prevents a vehicle from being re-matched right after a reroute attempt, so a single
/// crossing does not turn into a reroute loop. Roads cool down briefly; rail vehicles cool
/// down longer while the vanilla pathfinder recovers. Never serialized.
/// </summary>
public struct RerouteCooldown : IComponentData
{
    public byte m_Ticks;

    public RerouteCooldown(byte ticks) => m_Ticks = ticks;
}
