using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using NetSubLane = Game.Net.SubLane;

namespace RouteFilter.Systems;

/// <summary>
/// Coordinates exact-type rerouting. The selected network target is briefly exposed to the
/// vanilla pathfinder as blocked, then the affected vehicle's path is invalidated.
/// The barrier is reference-counted and removed as soon as rerouting completes.
/// </summary>
public sealed partial class VehicleDetourSystem : GameSystemBase
{
    private const byte BarrierWarmupTicks = 2;
    private const byte RequestTimeoutTicks = 64;
    private EntityQuery m_Requests;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Requests = GetEntityQuery(ComponentType.ReadWrite<VehicleDetourRequest>());
        RequireForUpdate(m_Requests);
    }

    protected override void OnUpdate()
    {
        using var vehicles = m_Requests.ToEntityArray(Allocator.Temp);
        foreach (var vehicle in vehicles)
        {
            var request = EntityManager.GetComponentData<VehicleDetourRequest>(vehicle);
            request.m_Ticks++;

            if (request.m_Ticks == BarrierWarmupTicks && EntityManager.TryGetComponent(vehicle, out PathOwner pathOwner))
            {
                pathOwner.m_State |= PathFlags.Obsolete;
                EntityManager.SetComponentData(vehicle, pathOwner);
                if (!EntityManager.HasComponent<Updated>(vehicle)) EntityManager.AddComponent<Updated>(vehicle);
            }

            var finished = request.m_Ticks > BarrierWarmupTicks + 1 && IsPathReady(vehicle);
            if (finished || request.m_Ticks >= RequestTimeoutTicks || !EntityManager.Exists(request.m_Target))
            {
                ReleaseRequest(vehicle, request.m_Target);
                continue;
            }

            EntityManager.SetComponentData(vehicle, request);
        }
    }

    private bool IsPathReady(Entity vehicle)
    {
        if (!EntityManager.TryGetComponent(vehicle, out PathOwner pathOwner)) return true;
        const PathFlags busy = PathFlags.Pending | PathFlags.Scheduled | PathFlags.Obsolete;
        return (pathOwner.m_State & busy) == 0;
    }

    private void ReleaseRequest(Entity vehicle, Entity target)
    {
        if (EntityManager.Exists(vehicle) && EntityManager.HasComponent<VehicleDetourRequest>(vehicle))
            EntityManager.RemoveComponent<VehicleDetourRequest>(vehicle);

        if (!EntityManager.Exists(target) || !EntityManager.TryGetComponent(target, out AccessDetourBlock block)) return;
        if (block.m_RequestCount > 1)
        {
            block.m_RequestCount--;
            EntityManager.SetComponentData(target, block);
            return;
        }

        EntityManager.RemoveComponent<AccessDetourBlock>(target);
        MarkTargetLanesUpdated(target);
    }

    private void MarkTargetLanesUpdated(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<NetSubLane> lanes)) return;
        foreach (var subLane in lanes)
            if (!EntityManager.HasComponent<Updated>(subLane.m_SubLane))
                EntityManager.AddComponent<Updated>(subLane.m_SubLane);
    }
}
