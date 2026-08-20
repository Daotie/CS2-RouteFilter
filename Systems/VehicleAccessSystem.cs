using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Vehicles;
using RouteFilter.Components;
using System.Collections.Generic;
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
    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private RestrictionIndexSystem m_Index = null!;
    private readonly List<Entity> m_PrefabBuffer = new();
    private int m_DetoursSinceReport;
    private int m_ReportTicks;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Cars = GetEntityQuery(
            ComponentType.ReadWrite<CarNavigation>(), ComponentType.ReadWrite<Moving>(),
            ComponentType.ReadOnly<CarNavigationLane>(), ComponentType.Exclude<Deleted>());
        m_Trains = GetEntityQuery(
            ComponentType.ReadWrite<TrainNavigation>(), ComponentType.ReadWrite<Moving>(),
            ComponentType.ReadOnly<TrainNavigationLane>(), ComponentType.Exclude<Deleted>());
        m_RestrictedNodes = GetEntityQuery(ComponentType.ReadOnly<NodeAssetRestrictionV1>());
        m_RestrictedSegments = GetEntityQuery(ComponentType.ReadOnly<SegmentAssetRestrictionV1>());
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
    }

    protected override void OnUpdate()
    {
        if (!Mod.Settings.EnableExactRestrictions ||
            (m_RestrictedNodes.IsEmptyIgnoreFilter && m_RestrictedSegments.IsEmptyIgnoreFilter)) return;
        ProcessCars();
        ProcessTrains();
        if (++m_ReportTicks >= 256)
        {
            if (m_DetoursSinceReport != 0)
                Mod.Log.Info($"Exact-asset enforcement requested {m_DetoursSinceReport} vehicle detours during the last reporting interval");
            m_DetoursSinceReport = 0;
            m_ReportTicks = 0;
        }
    }

    private void ProcessCars()
    {
        using var vehicles = m_Cars.ToEntityArray(Allocator.Temp);
        foreach (var vehicle in vehicles)
        {
            if (IsProtectedEmergency(vehicle) || !LoadVehiclePrefabs(vehicle) ||
                !ContainsAnyRestrictedPrefab() ||
                !TryGetBlockedTarget(vehicle, true, out var target)) continue;
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
            if (!LoadVehiclePrefabs(vehicle) ||
                !ContainsAnyRestrictedPrefab() ||
                !TryGetBlockedTarget(vehicle, false, out var target)) continue;
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

    private bool ContainsAnyRestrictedPrefab()
    {
        foreach (var prefab in m_PrefabBuffer)
            if (m_Index.ContainsRestrictedPrefab(prefab)) return true;
        return false;
    }

    private bool TryGetBlockedTarget(Entity vehicle, bool car, out Entity blockedTarget)
    {
        blockedTarget = Entity.Null;
        var limit = System.Math.Max(1, Mod.Settings.LookAheadLanes);
        if (car)
        {
            if (EntityManager.TryGetComponent(vehicle, out CarCurrentLane current) &&
                (TryGetBlockingTarget(current.m_Lane, out blockedTarget) ||
                 TryGetBlockingTarget(current.m_ChangeLane, out blockedTarget))) return true;
            var lanes = EntityManager.GetBuffer<CarNavigationLane>(vehicle, true);
            for (var i = 0; i < lanes.Length && i < limit; i++)
                if (TryGetBlockingTarget(lanes[i].m_Lane, out blockedTarget)) return true;
        }
        else
        {
            if (EntityManager.TryGetComponent(vehicle, out TrainCurrentLane current) &&
                (TryGetBlockingTarget(current.m_Front.m_Lane, out blockedTarget) ||
                 TryGetBlockingTarget(current.m_Rear.m_Lane, out blockedTarget))) return true;
            var lanes = EntityManager.GetBuffer<TrainNavigationLane>(vehicle, true);
            for (var i = 0; i < lanes.Length && i < limit; i++)
                if (TryGetBlockingTarget(lanes[i].m_Lane, out blockedTarget)) return true;
        }

        if (EntityManager.TryGetComponent(vehicle, out PathOwner pathOwner) &&
            EntityManager.TryGetBuffer(vehicle, true, out DynamicBuffer<PathElement> path))
        {
            var start = System.Math.Max(0, pathOwner.m_ElementIndex);
            for (var i = start; i < path.Length && i < start + limit; i++)
                if (TryGetBlockingTarget(path[i].m_Target, out blockedTarget)) return true;
        }
        return false;
    }

    private bool TryGetBlockingTarget(Entity lane, out Entity blockedTarget)
    {
        blockedTarget = Entity.Null;
        if (lane == Entity.Null) return false;

        // Fast path: the lane is directly owned by a restricted target.
        if (m_Index.TryGetRestrictedTarget(lane, out var directTarget) &&
            m_Index.TargetRestricts(directTarget, m_PrefabBuffer))
        {
            blockedTarget = directTarget;
            return true;
        }

        // Fallback that preserves the original Owner-chain semantics (for example
        // a lane on an unrestricted edge that ends at a restricted node).
        var entity = lane;
        for (var depth = 0; depth < 8 && entity != Entity.Null; depth++)
        {
            if (m_Index.TargetRestricts(entity, m_PrefabBuffer))
            {
                blockedTarget = entity;
                return true;
            }

            if (EntityManager.TryGetComponent(entity, out Game.Net.Edge edge))
            {
                if (m_Index.TargetRestricts(edge.m_Start, m_PrefabBuffer)) { blockedTarget = edge.m_Start; return true; }
                if (m_Index.TargetRestricts(edge.m_End, m_PrefabBuffer)) { blockedTarget = edge.m_End; return true; }
            }
            if (!EntityManager.TryGetComponent(entity, out Owner owner)) break;
            entity = owner.m_Owner;
        }
        return false;
    }

    private bool LoadVehiclePrefabs(Entity vehicle)
    {
        m_PrefabBuffer.Clear();
        AddPrefab(vehicle, m_PrefabBuffer);
        AddLayoutPrefabs(vehicle, m_PrefabBuffer);
        if (EntityManager.TryGetComponent(vehicle, out Controller controller) &&
            controller.m_Controller != Entity.Null && controller.m_Controller != vehicle)
        {
            AddPrefab(controller.m_Controller, m_PrefabBuffer);
            AddLayoutPrefabs(controller.m_Controller, m_PrefabBuffer);
        }
        return m_PrefabBuffer.Count != 0;
    }

    private void AddLayoutPrefabs(Entity controller, List<Entity> prefabs)
    {
        if (!EntityManager.TryGetBuffer(controller, true, out DynamicBuffer<LayoutElement> layout)) return;
        foreach (var element in layout) AddPrefab(element.m_Vehicle, prefabs);
    }

    private void AddPrefab(Entity vehicle, List<Entity> prefabs)
    {
        if (vehicle == Entity.Null || !EntityManager.TryGetComponent(vehicle, out PrefabRef prefabRef) ||
            prefabRef.m_Prefab == Entity.Null || prefabs.Contains(prefabRef.m_Prefab)) return;
        prefabs.Add(prefabRef.m_Prefab);
    }

    private void RequestDetour(Entity vehicle, Entity target)
    {
        if (EntityManager.HasComponent<VehicleDetourRequest>(vehicle)) return;

        EntityManager.AddComponentData(vehicle, new VehicleDetourRequest(target));
        m_DetoursSinceReport++;
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
