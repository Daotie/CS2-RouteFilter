using Game;
using Game.Common;
using Game.Net;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using RouteFilter.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CarLane = Game.Net.CarLane;

namespace RouteFilter.Systems;

/// <summary>
/// Phase 1D controlled primitive: at most one lease, explicitly armed, Safe-only.
/// O(new evaluations + active leases), no city query. No reroute or vehicle writes.
/// </summary>
public sealed partial class RestrictionLeaseSystem : GameSystemBase
{
    [BurstCompile]
    private struct LeaseJob : IJob
    {
        [ReadOnly] public NativeArray<RerouteSafetyEvaluation> Evaluations;
        [ReadOnly] public EntityStorageInfoLookup Entities;
        [ReadOnly] public ComponentLookup<Deleted> Deleted;
        [ReadOnly] public ComponentLookup<Temp> Temporary;
        [ReadOnly] public ComponentLookup<CarCurrentLane> CurrentLanes;
        [ReadOnly] public BufferLookup<CarNavigationLane> Navigation;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjects;
        public ComponentLookup<CarLane> Lanes;
        public NativeList<RouteFilterLaneLease> Leases;
        public NativeArray<uint> Counters; // admitted, restored, conflict, deleted, rejected
        public EntityCommandBuffer Commands;
        public Entity ArmedTarget;
        public uint Frame, Token;
        public int Revision;
        public bool ForceRelease;

        private bool Valid(Entity e) => e != Entity.Null && Entities.Exists(e) &&
            !Deleted.HasComponent(e) && !Temporary.HasComponent(e);

        public void Execute()
        {
            for (var i = Leases.Length - 1; i >= 0; i--)
            {
                var lease = Leases[i];
                if (!ForceRelease && Valid(lease.Target) && Valid(lease.Vehicle) &&
                    lease.RestrictionRevision == Revision &&
                    !LaneLeaseRules.HasExpired(Frame, lease.AbsoluteExpiryFrame) &&
                    Valid(lease.Lane)) continue;
                if (!Valid(lease.Lane) || !Lanes.HasComponent(lease.Lane)) Counters[3]++;
                else
                {
                    var lane = Lanes[lease.Lane];
                    if (LaneLeaseRules.OwnsCurrent(lease, lane.m_BlockageStart, lane.m_BlockageEnd))
                    {
                        lane.m_BlockageStart = lease.OriginalBlockageStart;
                        lane.m_BlockageEnd = lease.OriginalBlockageEnd;
                        Lanes[lease.Lane] = lane;
                        Commands.AddComponent<Updated>(lease.Lane);
                        Counters[1]++;
                    }
                    else Counters[2]++;
                }
                Leases.RemoveAtSwapBack(i);
            }
            if (ForceRelease || ArmedTarget == Entity.Null || Leases.Length != 0) return;
            for (var i = 0; i < Evaluations.Length; i++)
            {
                var e = Evaluations[i];
                if (e.m_Target != ArmedTarget) continue;
                // Never upgrade Unknown, stale observations or unmeasured latency to Safe.
                if (e.m_Verdict != RerouteSafetyVerdict.Safe ||
                    e.m_Confidence != SafetyConfidence.Calibrated ||
                    e.m_EvaluationFrame != Frame || e.m_RestrictionRevision != Revision ||
                    !Valid(e.m_Target) || !Valid(e.m_Vehicle) || !Valid(e.m_NextLane) ||
                    !Lanes.HasComponent(e.m_NextLane) ||
                    !CurrentLanes.HasComponent(e.m_Vehicle) ||
                    CurrentLanes[e.m_Vehicle].m_Lane != e.m_EntryLane ||
                    !LaneObjects.HasBuffer(e.m_NextLane) || LaneObjects[e.m_NextLane].Length != 0 ||
                    !math.isfinite(e.m_ExpectedLatencySeconds) || e.m_ExpectedLatencySeconds <= 0 ||
                    !math.isfinite(e.m_VanillaTimeStep) || e.m_VanillaTimeStep <= 0)
                { Counters[4]++; continue; }
                var lane = Lanes[e.m_NextLane];
                if (!LaneLeaseRules.IsEmpty(lane.m_BlockageStart, lane.m_BlockageEnd))
                { Counters[4]++; continue; }
                var ticks = math.ceil(e.m_ExpectedLatencySeconds / e.m_VanillaTimeStep);
                if (!math.isfinite(ticks) || ticks < 1 || ticks >= int.MaxValue / 2)
                { Counters[4]++; continue; }
                // Smallest nonzero representable interval at the next lane entry.
                // Reverse direction requires independent evidence; conservatively reject it.
                if (!Navigation.HasBuffer(e.m_Vehicle) || Navigation[e.m_Vehicle].Length == 0)
                { Counters[4]++; continue; }
                var next = Navigation[e.m_Vehicle][0];
                if (next.m_Lane != e.m_NextLane || next.m_CurvePosition.y <= next.m_CurvePosition.x)
                { Counters[4]++; continue; }
                var lease = new RouteFilterLaneLease
                {
                    Lane = e.m_NextLane, Target = e.m_Target, Vehicle = e.m_Vehicle,
                    RestrictionRevision = Revision,
                    OriginalBlockageStart = lane.m_BlockageStart,
                    OriginalBlockageEnd = lane.m_BlockageEnd,
                    WrittenBlockageStart = 0, WrittenBlockageEnd = 1,
                    CreatedFrame = Frame, AbsoluteExpiryFrame = Frame + (uint)ticks,
                    OwnerToken = Token, State = LaneLeaseState.Active
                };
                // Record ownership before writing; fixed capacity avoids hot-path growth.
                Leases.AddNoResize(lease);
                lane.m_BlockageStart = lease.WrittenBlockageStart;
                lane.m_BlockageEnd = lease.WrittenBlockageEnd;
                Lanes[e.m_NextLane] = lane;
                Commands.AddComponent<Updated>(lease.Lane);
                Counters[0]++;
                break;
            }
        }
    }

    private NativeList<RouteFilterLaneLease> m_Leases;
    private NativeArray<uint> m_Counters;
    private RestrictionSafetySystem m_Safety;
    private RestrictionIndexSystem m_Index;
    private SimulationSystem m_Simulation;
    private EndFrameBarrier m_Barrier;
    private UpdateSystem m_Update;
    private Entity m_ArmedTarget;
    private uint m_Token;
    private JobHandle m_Work;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Leases = new NativeList<RouteFilterLaneLease>(1, Allocator.Persistent);
        m_Counters = new NativeArray<uint>(5, Allocator.Persistent);
        m_Safety = World.GetOrCreateSystemManaged<RestrictionSafetySystem>();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_Barrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
        m_Update = World.GetOrCreateSystemManaged<UpdateSystem>();
    }

    // Explicit one-shot diagnostic arm. No automatic enforcement subscription.
    public void ArmSelectedTarget(Entity target)
    {
        ReleaseAll();
        m_ArmedTarget = target;
        m_Token++;
        Mod.Log.Info("[RouteFilter.Lease] One-shot probe armed; Unknown/Unsafe pass through. Graph publication is NOT VERIFIED.");
    }

    protected override void OnUpdate()
    {
        if (m_Update.currentPhase == SystemUpdatePhase.Serialize) { ReleaseAll(); return; }
        var probe = Mod.ConsumeLeaseProbeRequest();
        if (probe == 1) ArmSelectedTarget(World.GetOrCreateSystemManaged<RestrictionToolSystem>().SelectedTarget);
        else if (probe == 2) Report();
        if (!m_Work.IsCompleted) return; // No diagnostic-forced blocking Complete.
        m_Work.Complete();
        if (m_ArmedTarget == Entity.Null && m_Leases.Length == 0) return;
        var evaluations = m_Safety.GetEvaluations(out var safetyDependency);
        var job = new LeaseJob
        {
            Evaluations = evaluations, Entities = GetEntityStorageInfoLookup(),
            Deleted = GetComponentLookup<Deleted>(true), Temporary = GetComponentLookup<Temp>(true),
            CurrentLanes = GetComponentLookup<CarCurrentLane>(true),
            Navigation = GetBufferLookup<CarNavigationLane>(true),
            LaneObjects = GetBufferLookup<LaneObject>(true), Lanes = GetComponentLookup<CarLane>(),
            Leases = m_Leases, Counters = m_Counters,
            Commands = m_Barrier.CreateCommandBuffer(), ArmedTarget = m_ArmedTarget,
            Frame = m_Simulation.frameIndex, Token = m_Token, Revision = m_Index.Revision,
            ForceRelease = Mod.RestrictionsDirty
        };
        m_ArmedTarget = Entity.Null; // One attempt, no retry/refcount/extension.
        m_Work = job.Schedule(JobHandle.CombineDependencies(Dependency, safetyDependency));
        m_Safety.AddEvaluationReader(m_Work);
        m_Barrier.AddJobHandleForProducer(m_Work);
        Dependency = m_Work;
    }

    /// <summary>Lifecycle boundary only: synchronized release before native world serialization.</summary>
    public void ReleaseAll()
    {
        m_ArmedTarget = Entity.Null;
        if (!m_Leases.IsCreated) return;
        m_Work.Complete();
        Dependency.Complete();
        for (var i = m_Leases.Length - 1; i >= 0; i--)
        {
            var lease = m_Leases[i];
            if (!EntityManager.Exists(lease.Lane) || !EntityManager.HasComponent<CarLane>(lease.Lane))
            { m_Counters[3]++; continue; }
            var lane = EntityManager.GetComponentData<CarLane>(lease.Lane);
            if (!LaneLeaseRules.OwnsCurrent(lease, lane.m_BlockageStart, lane.m_BlockageEnd))
            { m_Counters[2]++; continue; }
            lane.m_BlockageStart = lease.OriginalBlockageStart;
            lane.m_BlockageEnd = lease.OriginalBlockageEnd;
            EntityManager.SetComponentData(lease.Lane, lane);
            if (!EntityManager.HasComponent<Updated>(lease.Lane)) EntityManager.AddComponent<Updated>(lease.Lane);
            m_Counters[1]++;
        }
        m_Leases.Clear();
    }

    public void Report()
    {
        if (!m_Work.IsCompleted) { Mod.Log.Info("[RouteFilter.Lease] Counters pending; no forced synchronization."); return; }
        m_Work.Complete();
        Mod.Log.Info($"[RouteFilter.Lease] admitted={m_Counters[0]}, restored={m_Counters[1]}, RestoreConflict={m_Counters[2]}, laneDeleted={m_Counters[3]}, rejected={m_Counters[4]}, active={m_Leases.Length}; publication=NOT_VERIFIED");
    }

    protected override void OnDestroy()
    {
        ReleaseAll();
        m_Leases.Dispose();
        m_Counters.Dispose();
        base.OnDestroy();
    }

    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
    {
        ReleaseAll();
        base.OnGamePreload(purpose, mode);
    }
}
