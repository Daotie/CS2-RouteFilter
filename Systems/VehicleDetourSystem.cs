using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Vehicles;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using NetSubLane = Game.Net.SubLane;

namespace RouteFilter.Systems;

/// <summary>
/// Coordinates exact-type rerouting. The selected network target is briefly exposed to the
/// vanilla pathfinder as blocked, then the affected vehicle's path is invalidated.
/// The barrier is reference-counted and removed as soon as rerouting completes.
/// When a recomputed path still has to cross the restricted target, no alternative route
/// exists: the vehicle is handed to the game's built-in cleanup by tagging it Deleted,
/// instead of being left to retry forever.
/// </summary>
public sealed partial class VehicleDetourSystem : GameSystemBase
{
    private const byte BarrierWarmupTicks = 2;
    private const byte RequestTimeoutTicks = 60;
    private EntityQuery m_Requests;
    private RestrictionIndexSystem m_Index = null!;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Requests = GetEntityQuery(ComponentType.ReadWrite<VehicleDetourRequest>());
        RequireForUpdate(m_Requests);
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
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

            if (request.m_Ticks > BarrierWarmupTicks + 1 && IsPathReady(vehicle))
            {
                if (IsPathStillBlocked(vehicle, request.m_Target))
                {
                    // Rerouting completed but the new path still crosses the restricted
                    // target: no alternative route exists, so remove the vehicle.
                    RemoveUnreachableVehicle(vehicle, request.m_Target);
                    continue;
                }
                ReleaseRequest(vehicle, request.m_Target);
                continue;
            }

            if (!EntityManager.Exists(request.m_Target))
            {
                ReleaseRequest(vehicle, request.m_Target);
                continue;
            }

            if (request.m_Ticks >= RequestTimeoutTicks)
            {
                // The reroute did not finish within ~1 second. If the vanilla pathfinder
                // failed, or the vehicle is stopped and cannot progress, no usable
                // alternative exists: hand it to the game's cleanup instead of retrying.
                if (HasFailedPath(vehicle) || IsNotMoving(vehicle))
                {
                    RemoveUnreachableVehicle(vehicle, request.m_Target);
                    continue;
                }
                ReleaseRequest(vehicle, request.m_Target);
                continue;
            }

            EntityManager.SetComponentData(vehicle, request);
        }
    }

    private bool IsNotMoving(Entity vehicle)
    {
        if (!EntityManager.TryGetComponent(vehicle, out Moving moving)) return false;
        return math.lengthsq(moving.m_Velocity) < 0.01f;
    }

    private bool IsPathStillBlocked(Entity vehicle, Entity target)
    {
        if (target == Entity.Null || !EntityManager.Exists(target)) return false;
        if (!EntityManager.TryGetComponent(vehicle, out PathOwner pathOwner) ||
            !EntityManager.TryGetBuffer(vehicle, true, out DynamicBuffer<PathElement> path)) return false;

        var start = System.Math.Max(0, pathOwner.m_ElementIndex);
        for (var i = start; i < path.Length; i++)
            if (m_Index.TryGetRestrictedTarget(path[i].m_Target, out var restricted) &&
                restricted == target)
                return true;
        return false;
    }

    private bool HasFailedPath(Entity vehicle)
    {
        if (!EntityManager.TryGetComponent(vehicle, out PathOwner pathOwner)) return false;
        return VehicleUtils.PathfindFailed(pathOwner);
    }

    private void RemoveUnreachableVehicle(Entity vehicle, Entity target)
    {
        ReleaseRequest(vehicle, target);
        if (!EntityManager.Exists(vehicle)) return;

        // Tag the whole consist with the game's built-in Deleted marker; the vanilla
        // cleanup systems then despawn it and settle any resources it carries.
        if (EntityManager.TryGetBuffer(vehicle, true, out DynamicBuffer<LayoutElement> layout))
        {
            for (var i = 0; i < layout.Length; i++)
            {
                var part = layout[i].m_Vehicle;
                if (part != vehicle && !EntityManager.HasComponent<Deleted>(part))
                    EntityManager.AddComponent<Deleted>(part);
            }
        }
        if (!EntityManager.HasComponent<Deleted>(vehicle))
            EntityManager.AddComponent<Deleted>(vehicle);

        Mod.Log.Info($"Removed unreachable vehicle {vehicle.Index}:{vehicle.Version}: no valid alternative route around restricted target {target.Index}:{target.Version}");
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
