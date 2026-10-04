using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Tools;
using Game.Vehicles;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using NetEdge = Game.Net.Edge;

namespace RouteFilterCleanup
{
// One request for the whole city. Native systems still run in dependency order:
// owners -> lanes/path graph -> vehicles. No roads or lanes are manually deleted.
public sealed class RoadNetworkRefreshSystem : GameSystemBase
{
    private const int MinimumOwnerTicks = 120;
    private const int RequiredQuietTicks = 20;
    private const int MaximumTicksPerStage = 1200;

    private enum RefreshStage { Idle, Owners, Lanes }

    private EntityQuery m_RoadEdges;
    private EntityQuery m_RoadNodes;
    private EntityQuery m_RoadOwnedLanes;
    private EntityQuery m_UpdatedRoads;
    private EntityQuery m_UpdatedLanes;
    private EntityQuery m_CarsWithPaths;
    private RefreshStage m_Stage;
    private int m_StageTicks;
    private int m_QuietTicks;
    private int m_RoadEdgeCount;
    private int m_RoadNodeCount;
    private int m_LaneCount;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_RoadEdges = GetEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<NetEdge>());
        m_RoadNodes = GetEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Node>());
        m_RoadOwnedLanes = GetEntityQuery(ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Owner>());
        m_UpdatedRoads = GetEntityQuery(ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Updated>());
        m_UpdatedLanes = GetEntityQuery(ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Updated>());
        m_CarsWithPaths = GetEntityQuery(ComponentType.ReadOnly<Car>(), ComponentType.ReadOnly<PathOwner>());
    }

    protected override void OnGameLoaded(Context serializationContext) => ResetProgress();

    protected override void OnUpdate()
    {
        if (Mod.ConsumeNetworkRefreshRequest()) Start();
        if (m_Stage != RefreshStage.Idle) Advance();
    }

    private void Start()
    {
        if (m_Stage != RefreshStage.Idle)
        {
            Mod.Log.Warn("[RouteFilterCleanup] FULL_REFRESH ignored: already in progress");
            return;
        }

        var nodes = new HashSet<Entity>();
        var edges = 0;
        using (var roadEdges = m_RoadEdges.ToEntityArray(Allocator.Temp))
        {
            foreach (var entity in roadEdges)
            {
                if (!IsValid(entity)) continue;
                var edge = EntityManager.GetComponentData<NetEdge>(entity);
                if (IsValidNode(edge.m_Start)) nodes.Add(edge.m_Start);
                if (IsValidNode(edge.m_End)) nodes.Add(edge.m_End);
                if (!EntityManager.HasComponent<Updated>(entity)) EntityManager.AddComponent<Updated>(entity);
                edges++;
            }
        }
        using (var roadNodes = m_RoadNodes.ToEntityArray(Allocator.Temp))
        {
            foreach (var entity in roadNodes)
                if (IsValidNode(entity)) nodes.Add(entity);
        }
        foreach (var node in nodes)
            if (!EntityManager.HasComponent<Updated>(node)) EntityManager.AddComponent<Updated>(node);

        if (edges == 0)
        {
            Mod.Log.Warn("[RouteFilterCleanup] FULL_REFRESH aborted: no valid road edges found");
            return;
        }
        m_RoadEdgeCount = edges;
        m_RoadNodeCount = nodes.Count;
        m_LaneCount = 0;
        m_Stage = RefreshStage.Owners;
        m_StageTicks = 0;
        m_QuietTicks = 0;
        Mod.Log.Info($"[RouteFilterCleanup] FULL_REFRESH started build={Mod.BuildId} roadEdges={edges} roadNodes={nodes.Count}; all owners submitted together to native geometry/lane systems");
    }

    private void Advance()
    {
        m_StageTicks++;
        if (m_Stage == RefreshStage.Owners && m_StageTicks < MinimumOwnerTicks) return;
        if (m_UpdatedRoads.IsEmptyIgnoreFilter && m_UpdatedLanes.IsEmptyIgnoreFilter) m_QuietTicks++;
        else m_QuietTicks = 0;
        if (m_QuietTicks < RequiredQuietTicks)
        {
            if (m_StageTicks >= MaximumTicksPerStage)
            {
                Mod.Log.Warn($"[RouteFilterCleanup] FULL_REFRESH aborted: stage={m_Stage} did not settle after {m_StageTicks} ticks; vehicle paths untouched");
                ResetProgress();
            }
            return;
        }

        if (m_Stage == RefreshStage.Owners)
        {
            RepublishAllRoadLanes();
            m_Stage = RefreshStage.Lanes;
            m_StageTicks = 0;
            m_QuietTicks = 0;
            return;
        }
        RefreshAllCarsOnce();
        ResetProgress();
    }

    private void RepublishAllRoadLanes()
    {
        using var lanes = m_RoadOwnedLanes.ToEntityArray(Allocator.Temp);
        foreach (var lane in lanes)
        {
            if (!IsValid(lane)) continue;
            var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            if (!EntityManager.Exists(owner) || !EntityManager.HasComponent<Road>(owner)) continue;
            if (!EntityManager.HasComponent<Updated>(lane)) EntityManager.AddComponent<Updated>(lane);
            m_LaneCount++;
        }
        Mod.Log.Info($"[RouteFilterCleanup] FULL_REFRESH owners settled after {m_StageTicks} ticks; roadOwnedLanes={m_LaneCount} submitted together to native path graph");
    }

    private void RefreshAllCarsOnce()
    {
        var seen = 0;
        var marked = 0;
        var laneReattached = 0;
        var alreadyBusy = 0;
        using var cars = m_CarsWithPaths.ToEntityArray(Allocator.Temp);
        foreach (var car in cars)
        {
            if (!IsValid(car)) continue;
            seen++;
            var path = EntityManager.GetComponentData<PathOwner>(car);
            if ((path.m_State & (PathFlags.Pending | PathFlags.Scheduled | PathFlags.Obsolete)) != 0) alreadyBusy++;
            path.m_State |= PathFlags.Obsolete;
            EntityManager.SetComponentData(car, path);
            marked++;
            if (!EntityManager.HasComponent<CarCurrentLane>(car)) continue;
            var current = EntityManager.GetComponentData<CarCurrentLane>(car);
            current.m_LaneFlags |= Game.Vehicles.CarLaneFlags.Obsolete;
            EntityManager.SetComponentData(car, current);
            laneReattached++;
        }
        Mod.Log.Info($"[RouteFilterCleanup] FULL_REFRESH completed build={Mod.BuildId} roadEdges={m_RoadEdgeCount} roadNodes={m_RoadNodeCount} roadOwnedLanes={m_LaneCount} carsSeen={seen} pathsMarkedSameTick={marked} laneReattachRequested={laneReattached} alreadyBusy={alreadyBusy}; no road/lane deletion and no blockage/Transform/speed writes");
    }

    private bool IsValid(Entity entity) =>
        EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity) &&
        !EntityManager.HasComponent<Temp>(entity);

    private bool IsValidNode(Entity entity) => IsValid(entity) && EntityManager.HasComponent<Node>(entity);

    private void ResetProgress()
    {
        m_Stage = RefreshStage.Idle;
        m_StageTicks = 0;
        m_QuietTicks = 0;
    }
}
}
