using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Vehicles;
using RouteFilter.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilterCleanup
{

public sealed class CleanupSystem : GameSystemBase
{
    private EntityQuery m_Nodes;
    private EntityQuery m_Segments;
    private EntityQuery m_Assets;
    private EntityQuery m_Blocks;
    private EntityQuery m_Requests;
    private EntityQuery m_Cooldowns;
    private EntityQuery m_CarLanes;
    private bool m_Audited;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Nodes = GetEntityQuery(ComponentType.ReadOnly<NodeAssetRestrictionV1>());
        m_Segments = GetEntityQuery(ComponentType.ReadOnly<SegmentAssetRestrictionV1>());
        m_Assets = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_Blocks = GetEntityQuery(ComponentType.ReadOnly<AccessDetourBlock>());
        m_Requests = GetEntityQuery(ComponentType.ReadOnly<VehicleDetourRequest>());
        m_Cooldowns = GetEntityQuery(ComponentType.ReadOnly<RerouteCooldown>());
        m_CarLanes = GetEntityQuery(ComponentType.ReadOnly<CarLane>());
    }

    protected override void OnUpdate()
    {
        if (!m_Audited && m_CarLanes.CalculateEntityCount() > 0)
        {
            m_Audited = true;
            Report("READ_ONLY_AUDIT");
            ReportSuspiciousLanes();
        }
        if (Mod.ConsumeScanRequest()) ReportSuspiciousLanes();
        if (!Mod.ConsumeCleanupRequest()) return;

        var targetEntities = new HashSet<Entity>();
        AddEntities(m_Nodes, targetEntities);
        AddEntities(m_Segments, targetEntities);
        AddEntities(m_Assets, targetEntities);
        foreach (var entity in targetEntities)
        {
            if (EntityManager.HasComponent<NodeAssetRestrictionV1>(entity)) EntityManager.RemoveComponent<NodeAssetRestrictionV1>(entity);
            if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(entity)) EntityManager.RemoveComponent<SegmentAssetRestrictionV1>(entity);
            if (EntityManager.HasComponent<RestrictedVehicleAssetV1>(entity)) EntityManager.RemoveComponent<RestrictedVehicleAssetV1>(entity);
        }

        var blocks = Remove<AccessDetourBlock>(m_Blocks);
        var requests = Remove<VehicleDetourRequest>(m_Requests);
        var cooldowns = Remove<RerouteCooldown>(m_Cooldowns);
        Mod.Log.Info($"[RouteFilterCleanup] SAFE_CLEANUP completed targets={targetEntities.Count}, blocks={blocks}, requests={requests}, cooldowns={cooldowns}; pathOwnerWrites=0; carLaneWrites=0; unknownStateWrites=0");
        Report("AFTER_SAFE_CLEANUP");
        ReportSuspiciousLanes();
    }

    private void Report(string phase)
    {
        Mod.Log.Info($"[RouteFilterCleanup] {phase}: nodes={m_Nodes.CalculateEntityCount()}, segments={m_Segments.CalculateEntityCount()}, restrictedAssetBuffers={m_Assets.CalculateEntityCount()}, legacyBlocks={m_Blocks.CalculateEntityCount()}, legacyRequests={m_Requests.CalculateEntityCount()}, cooldowns={m_Cooldowns.CalculateEntityCount()}, nonDefaultCarLaneBlockage={CountNonDefaultBlockage()}; PathOwner/Updated/CarLane state was not modified");
    }

    private int CountNonDefaultBlockage()
    {
        var count = 0;
        using var lanes = m_CarLanes.ToComponentDataArray<CarLane>(Allocator.Temp);
        foreach (var lane in lanes)
            // CarLane uses the invalid interval 255/0 as the vanilla no-blockage
            // sentinel. Only an ordered byte interval represents an active blockage.
            if (HasActiveBlockage(lane.m_BlockageStart, lane.m_BlockageEnd)) count++;
        return count;
    }

    private sealed class LaneStats
    {
        public Entity Lane;
        public Entity Owner;
        public int LaneObjects;
        public int MovingObjects;
        public int StoppedObjects;
        public int DrivingObjects;
        public int StaleCurrentLane;
        public float BlockageStart;
        public float BlockageEnd;
        public bool HasActiveBlockage;
        public float Length;
        public float3 Position;
    }

    private void ReportSuspiciousLanes()
    {
        var lanes = new List<LaneStats>();
        using var laneEntities = m_CarLanes.ToEntityArray(Allocator.Temp);
        foreach (var laneEntity in laneEntities)
        {
            if (EntityManager.HasComponent<Deleted>(laneEntity)) continue;
            var carLane = EntityManager.GetComponentData<CarLane>(laneEntity);
            var owner = EntityManager.HasComponent<Owner>(laneEntity)
                ? EntityManager.GetComponentData<Owner>(laneEntity).m_Owner
                : Entity.Null;
            if (owner == Entity.Null) continue;

            var stat = new LaneStats
            {
                Lane = laneEntity,
                Owner = owner,
                BlockageStart = carLane.m_BlockageStart,
                BlockageEnd = carLane.m_BlockageEnd,
                HasActiveBlockage = HasActiveBlockage(carLane.m_BlockageStart, carLane.m_BlockageEnd)
            };
            if (EntityManager.HasComponent<Curve>(laneEntity))
            {
                var curve = EntityManager.GetComponentData<Curve>(laneEntity);
                stat.Length = curve.m_Length;
                stat.Position = (curve.m_Bezier.a + curve.m_Bezier.d) * 0.5f;
            }
            if (EntityManager.HasBuffer<LaneObject>(laneEntity))
            {
                var objects = EntityManager.GetBuffer<LaneObject>(laneEntity, true);
                stat.LaneObjects = objects.Length;
                foreach (var laneObject in objects)
                {
                    var vehicle = laneObject.m_LaneObject;
                    if (!EntityManager.Exists(vehicle) || EntityManager.HasComponent<Deleted>(vehicle))
                    {
                        stat.StaleCurrentLane++;
                        continue;
                    }
                    if (EntityManager.HasComponent<CarCurrentLane>(vehicle) &&
                        EntityManager.GetComponentData<CarCurrentLane>(vehicle).m_Lane != laneEntity)
                        stat.StaleCurrentLane++;
                    if (!EntityManager.HasComponent<Moving>(vehicle)) continue;
                    stat.MovingObjects++;
                    var speed = math.length(EntityManager.GetComponentData<Moving>(vehicle).m_Velocity);
                    if (speed < 0.25f) stat.StoppedObjects++;
                    if (speed > 2f) stat.DrivingObjects++;
                }
            }
            lanes.Add(stat);
        }

        var candidates = new List<Tuple<LaneStats, LaneStats>>();
        foreach (var group in lanes.GroupBy(x => x.Owner))
        {
            var siblings = group.ToList();
            if (siblings.Count < 2) continue;
            foreach (var lane in siblings)
            {
                if (lane.MovingObjects < 5 || lane.StoppedObjects * 100 < lane.MovingObjects * 80) continue;
                var neighbor = siblings
                    .Where(x => x.Lane != lane.Lane && x.DrivingObjects > 0)
                    .OrderByDescending(x => x.DrivingObjects)
                    .FirstOrDefault();
                if (neighbor != null) candidates.Add(Tuple.Create(lane, neighbor));
            }
        }

        Mod.Log.Info($"[RouteFilterCleanup] LANE_DIAGNOSTIC lanes={lanes.Count}, candidates={candidates.Count}; criteria=vehicles>=5, stopped>=80%, siblingDriving>0; writes=0");
        foreach (var pair in candidates.OrderByDescending(x => x.Item1.StoppedObjects).Take(50))
        {
            var lane = pair.Item1;
            var sibling = pair.Item2;
            Mod.Log.Info(
                $"[RouteFilterCleanup] SUSPICIOUS lane={Format(lane.Lane)} owner={Format(lane.Owner)} " +
                $"pos=({lane.Position.x:F1},{lane.Position.y:F1},{lane.Position.z:F1}) len={lane.Length:F1} " +
                $"objects={lane.LaneObjects} moving={lane.MovingObjects} stopped={lane.StoppedObjects} driving={lane.DrivingObjects} stale={lane.StaleCurrentLane} " +
                $"blockage={lane.BlockageStart:F0}/{lane.BlockageEnd:F0} active={lane.HasActiveBlockage}; sibling={Format(sibling.Lane)} " +
                $"objects={sibling.LaneObjects} stopped={sibling.StoppedObjects} driving={sibling.DrivingObjects} " +
                $"blockage={sibling.BlockageStart:F0}/{sibling.BlockageEnd:F0} active={sibling.HasActiveBlockage}");
        }
    }

    private static string Format(Entity entity) => $"{entity.Index}:{entity.Version}";
    private static bool HasActiveBlockage(byte start, byte end) => start <= end;

    private void AddEntities(EntityQuery query, HashSet<Entity> target)
    {
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities) target.Add(entity);
    }

    private int Remove<T>(EntityQuery query) where T : unmanaged, IComponentData
    {
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
            if (EntityManager.HasComponent<T>(entity)) EntityManager.RemoveComponent<T>(entity);
        return entities.Length;
    }
}
}
