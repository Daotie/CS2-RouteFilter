using Game;
using Game.Common;
using Game.Pathfind;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using RouteFilter.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace RouteFilter.Systems;

/// <summary>O(new candidates + 64 active approaches). No physical blockage or global scan.
/// Query-local graph exclusion is executed and restored by RestrictionPathfindHook.</summary>
public sealed partial class RoadEnforcementCoordinator : GameSystemBase
{
    private const int MaxAttempts = 64;
    private const uint DeadlineFrames = 240;
    [BurstCompile]
    private struct AdmitJob : IJob
    {
        [ReadOnly] public NativeArray<RerouteSafetyEvaluation> Evaluations;
        [ReadOnly] public EntityStorageInfoLookup Entities;
        [ReadOnly] public ComponentLookup<Deleted> Deleted;
        [ReadOnly] public ComponentLookup<Temp> Temporary;
        [ReadOnly] public ComponentLookup<Train> Trains;
        [ReadOnly] public ComponentLookup<CarCurrentLane> Current;
        [ReadOnly] public ComponentLookup<Game.Common.Target> Destinations;
        [ReadOnly] public BufferLookup<CarNavigationLane> Navigation;
        public ComponentLookup<PathOwner> Owners;
        public NativeList<EnforcementAttempt> Attempts;
        public NativeParallelHashMap<Entity, int> ByVehicle;
        public NativeArray<uint> Counters;
        public uint Frame;
        public int Revision;
        public bool EmergencyProtection;
        private bool Valid(Entity e) => e != Entity.Null && Entities.Exists(e) && !Deleted.HasComponent(e) && !Temporary.HasComponent(e);
        public void Execute()
        {
            for (var i = Attempts.Length - 1; i >= 0; i--)
            {
                var attempt = Attempts[i];
                if (!Valid(attempt.Vehicle) || !Valid(attempt.Target) || attempt.RestrictionRevision != Revision ||
                    !Current.TryGetComponent(attempt.Vehicle, out var current) || current.m_Lane != attempt.GateEntryLane ||
                    EnforcementPolicy.HasExpired(Frame, attempt.AbsoluteDeadlineFrame))
                {
                    if (Owners.TryGetComponent(attempt.Vehicle, out var owner) && EnforcementPolicy.OwnsRequest(attempt, owner.m_State, owner.m_ElementIndex))
                    { owner.m_State = attempt.OriginalPathState; Owners[attempt.Vehicle] = owner; }
                    ByVehicle.Remove(attempt.Vehicle); Attempts.RemoveAtSwapBack(i);
                    if (i < Attempts.Length) ByVehicle[Attempts[i].Vehicle] = i;
                    continue;
                }
                if (attempt.State == EnforcementAttemptState.Requested && Owners.TryGetComponent(attempt.Vehicle, out var result) &&
                    (result.m_State & (PathFlags.Pending | PathFlags.Scheduled | PathFlags.Obsolete)) == 0)
                {
                    attempt.State = (result.m_State & (PathFlags.Failed | PathFlags.Stuck)) == 0 ? EnforcementAttemptState.Resolved : EnforcementAttemptState.Unresolved;
                    Counters[attempt.State == EnforcementAttemptState.Resolved ? 7 : 8]++; Attempts[i] = attempt;
                }
            }
            for (var i = 0; i < Evaluations.Length; i++)
            {
                var v = Evaluations[i];
                if (v.m_Verdict != RerouteSafetyVerdict.Safe) { Counters[9]++; continue; }
                if (!Valid(v.m_Vehicle) || !Valid(v.m_Target) || Trains.HasComponent(v.m_Vehicle) || v.m_RestrictionRevision != Revision ||
                    Frame - v.m_EvaluationFrame > 2 || Frame - v.m_FirstSeenFrame > DeadlineFrames ||
                    !Current.TryGetComponent(v.m_Vehicle, out var current) || current.m_Lane != v.m_EntryLane ||
                    current.m_ChangeLane != Entity.Null || current.m_ChangeProgress != 0 ||
                    !Navigation.TryGetBuffer(v.m_Vehicle, out var nav) ||
                    (v.m_ViaLane == Entity.Null ? nav.Length == 0 || nav[0].m_Lane != v.m_NextLane :
                        nav.Length < 2 || nav[0].m_Lane != v.m_ViaLane || nav[1].m_Lane != v.m_NextLane))
                { Counters[14]++; continue; }
                if (EmergencyProtection && EnforcementPolicy.IsExempt(v.m_Category)) { Counters[12]++; continue; }
                if (ByVehicle.ContainsKey(v.m_Vehicle)) { Counters[10]++; continue; }
                if (Attempts.Length >= MaxAttempts) { Counters[17]++; continue; }
                if (!Destinations.TryGetComponent(v.m_Vehicle, out var destination) || destination.m_Target == Entity.Null) continue;
                if (Frame - Counters[16] >= 64) { Counters[16] = Frame; Counters[18] = 0; }
                if (Counters[18] >= 4) { Counters[15]++; continue; }
                if (!Owners.TryGetComponent(v.m_Vehicle, out var owner) || !EnforcementPolicy.CanRequestReroute(owner.m_State)) { Counters[11]++; continue; }
                var before = owner.m_State; owner.m_State |= PathFlags.Obsolete; Owners[v.m_Vehicle] = owner;
                ByVehicle.TryAdd(v.m_Vehicle, Attempts.Length);
                Attempts.Add(new EnforcementAttempt { Vehicle = v.m_Vehicle, Target = v.m_Target, GateEntryLane = v.m_EntryLane, ViaLane = v.m_ViaLane,
                    OwnedLane = v.m_NextLane, RestrictionRevision = Revision, OriginalPathState = before, WrittenPathState = owner.m_State,
                    OriginalElementIndex = owner.m_ElementIndex, RequestedFrame = Frame, AbsoluteDeadlineFrame = Frame + DeadlineFrames,
                    Braking = v.m_Braking, GeometryLength = v.m_VehicleGeometryLength,
                    NativeDestination = destination.m_Target, TraversalEnd = current.m_CurvePosition.z,
                    Forward = current.m_CurvePosition.z > current.m_CurvePosition.x,
                    Backend = EnforcementBackend.Road, State = EnforcementAttemptState.Requested, Category = v.m_Category });
                Counters[5]++; Counters[6]++; Counters[18]++;
            }
        }
    }
    private NativeList<EnforcementAttempt> m_Attempts;
    private NativeParallelHashMap<Entity, int> m_ByVehicle;
    private NativeArray<uint> m_Counters;
    private RestrictionSafetySystem m_Safety;
    private RestrictionIndexSystem m_Index;
    private SimulationSystem m_Simulation;
    private UpdateSystem m_Update;
    private JobHandle m_Work;
    public bool EnforcementEnabled { get; set; } = true;
    public int ActiveLeases => 0;
    public int ActiveAttempts { get; private set; }
    protected override void OnCreate()
    {
        base.OnCreate();
        m_Attempts = new NativeList<EnforcementAttempt>(MaxAttempts, Allocator.Persistent);
        m_ByVehicle = new NativeParallelHashMap<Entity, int>(MaxAttempts, Allocator.Persistent);
        m_Counters = new NativeArray<uint>(20, Allocator.Persistent);
        m_Safety = World.GetOrCreateSystemManaged<RestrictionSafetySystem>(); m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>(); m_Update = World.GetOrCreateSystemManaged<UpdateSystem>();
    }
    protected override void OnDestroy()
    { ReleaseAll(); m_Attempts.Dispose(); m_ByVehicle.Dispose(); m_Counters.Dispose(); base.OnDestroy(); }
    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
    { ReleaseAll(); base.OnGamePreload(purpose, mode); }
    protected override void OnUpdate()
    {
        RestrictionPathfindHook.RetireCompleted();
        if (m_Update.currentPhase == SystemUpdatePhase.Serialize) { ReleaseAll(); return; }
        if (!m_Work.IsCompleted) return;
        m_Work.Complete(); ActiveAttempts = m_Attempts.Length;
        if (!EnforcementEnabled || !RestrictionPathfindHook.Available || Mod.Settings?.EnableRoadEnforcement == false ||
            !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable || Mod.RestrictionsDirty)
        { if (m_Attempts.Length > 0) ReleaseAll(); return; }
        if (m_Index.ActiveTargetCount == 0 && m_Attempts.Length == 0) return;
        // EvaluationCount returns zero while fresh work is pending; use its deferred array/dependency.
        var evaluations = m_Safety.GetEvaluations(out var safetyDependency);
        m_Work = new AdmitJob { Evaluations = evaluations, Entities = GetEntityStorageInfoLookup(), Deleted = GetComponentLookup<Deleted>(true),
            Temporary = GetComponentLookup<Temp>(true), Trains = GetComponentLookup<Train>(true), Current = GetComponentLookup<CarCurrentLane>(true),
            Navigation = GetBufferLookup<CarNavigationLane>(true), Owners = GetComponentLookup<PathOwner>(), Attempts = m_Attempts,
            Destinations = GetComponentLookup<Game.Common.Target>(true),
            ByVehicle = m_ByVehicle, Counters = m_Counters, Frame = m_Simulation.frameIndex, Revision = m_Index.Revision,
            EmergencyProtection = Mod.Settings?.EmergencyProtection ?? true }.Schedule(JobHandle.CombineDependencies(Dependency, safetyDependency));
        m_Safety.AddEvaluationReader(m_Work); Dependency = m_Work;
    }
    internal bool TryGetRequest(Entity vehicle, out EnforcementAttempt attempt)
    {
        attempt = default;
        if (!m_Work.IsCompleted) return false;
        m_Work.Complete();
        if (!m_ByVehicle.TryGetValue(vehicle, out var index)) return false;
        attempt = m_Attempts[index]; return attempt.State == EnforcementAttemptState.Requested && !attempt.QueryIntercepted;
    }
    internal void MarkIntercepted(Entity vehicle)
    { if (m_ByVehicle.TryGetValue(vehicle, out var i)) { var a = m_Attempts[i]; a.QueryIntercepted = true; m_Attempts[i] = a; } }
    public void ReleaseAll()
    {
        if (!m_Attempts.IsCreated) return;
        m_Work.Complete(); Dependency.Complete(); RestrictionPathfindHook.ReleaseAll();
        for (var i = 0; i < m_Attempts.Length; i++)
        {
            var attempt = m_Attempts[i];
            if (!EntityManager.Exists(attempt.Vehicle) || !EntityManager.HasComponent<PathOwner>(attempt.Vehicle)) continue;
            var owner = EntityManager.GetComponentData<PathOwner>(attempt.Vehicle);
            if (!EnforcementPolicy.OwnsRequest(attempt, owner.m_State, owner.m_ElementIndex)) continue;
            owner.m_State = attempt.OriginalPathState; EntityManager.SetComponentData(attempt.Vehicle, owner);
        }
        m_Attempts.Clear(); m_ByVehicle.Clear(); ActiveAttempts = 0;
    }
    public void ResetRuntimeState() { ReleaseAll(); for (var i = 0; i < m_Counters.Length; i++) m_Counters[i] = 0; }
    public void CopyCounters(NativeArray<uint> destination)
    {
        if (!destination.IsCreated || destination.Length < 20 || !m_Work.IsCompleted) return;
        m_Work.Complete(); for (var i = 0; i < 20; i++) destination[i] = m_Counters[i];
    }
}
