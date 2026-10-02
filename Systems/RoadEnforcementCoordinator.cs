using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
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
/// Road enforcement backend. Owns the whole RouteFilter-side lifecycle for road vehicles:
/// candidate -&gt; safety -&gt; one vanilla reroute request -&gt; bounded temporary lane lease -&gt; release.
///
/// WORKLOAD GROWS WITH: watched road entry lanes plus the LaneObjects on them (upstream), and new
/// safe candidates plus active leases and active attempts (here). It never queries vehicles or
/// lanes by archetype, so its cost does not scale with city size.
/// FAST PATH: no watched entry lanes means the system returns before scheduling anything.
/// COMPLEXITY: O(new safe candidates + active leases + active attempts); the attempt and lease
/// stores are hard-capped, so the in-job searches are bounded by that cap rather than by traffic.
/// ALLOCATIONS: none after construction. Stores are persistent native containers that are
/// cleared, never reallocated.
/// JOB DEPENDENCIES: one job per frame, chained after the safety job through the evaluation
/// dependency. No per-frame Complete.
/// MAIN THREAD SYNC: already-completed jobs are retired without waiting. Save, Reset,
/// unload and Dispose explicitly complete owned work before releasing mutations.
/// STRUCTURAL CHANGES: one <c>Updated</c> add per lane the graph publication actually needs, and
/// only at acquire/release boundaries.
/// </summary>
/// <remarks>
/// Failure policy is uniform: anything that is not provably safe results in RouteFilter releasing
/// its own state and letting the vehicle through. RouteFilter never stops a vehicle, never blocks
/// a lane permanently, never retries, and never extends a lease.
/// </remarks>
public sealed partial class RoadEnforcementCoordinator : GameSystemBase
{
    /// <summary>Hard ceilings. Exceeding them drops the request for that frame; nothing is queued.</summary>
    private const int kMaxLeases = 32;
    private const int kMaxAttempts = 64;

    /// <summary>Absolute lease lifetime. Never extended, never renewed, never referenced.</summary>
    private const uint kLeaseLifetimeFrames = 30;

    /// <summary>Absolute attempt deadline. After this the attempt is closed as unresolved.</summary>
    private const uint kAttemptDeadlineFrames = 240;

    /// <summary>
    /// City-wide ceiling on lane graph publications per second. A lane write publishes
    /// <c>RuleFlags.HasBlockage</c> for every pathfinder in the city, so an unbounded rate is
    /// what turns a restriction into a frame-time collapse. Requests over budget are dropped,
    /// not queued, and the affected vehicle is grandfathere.
    /// </summary>
    private const uint kMaxGraphMutationsPerSecond = 8;

    private const uint kSecondInFrames = 60; // Budget window in simulation frames; not wall-clock seconds.

    // Counter slots. Fixed layout so diagnostics never allocate.
    private const int cLeasesAcquired = 0;
    private const int cLeasesReleased = 1;
    private const int cLeasesReasserted = 2;
    private const int cLeaseRestoreConflict = 3;
    private const int cLeasesLaneDeleted = 4;
    private const int cReroutesRequested = 5;
    private const int cAttemptsOpened = 6;
    private const int cAttemptsResolved = 7;
    private const int cAttemptsUnresolved = 8;
    private const int cRefusedNotSafe = 9;
    private const int cRefusedAlreadyActive = 10;
    private const int cRefusedPathBusy = 11;
    private const int cRefusedExempt = 12;
    private const int cRefusedFixedRoute = 13;
    private const int cRefusedTopology = 14;
    private const int cRefusedBudget = 15;
    private const int cRefusedLaneOccupied = 16;
    private const int cRefusedStoreFull = 17;
    private const int cRefusedStaleRevision = 18;
    private const int cGrandfathered = 19;
    private const int cCounterCount = 20;

    [BurstCompile]
    private struct CoordinateJob : IJob
    {
        [ReadOnly] public NativeArray<RerouteSafetyEvaluation> Evaluations;
        [ReadOnly] public EntityStorageInfoLookup Entities;
        [ReadOnly] public ComponentLookup<Deleted> Deleted;
        [ReadOnly] public ComponentLookup<Temp> Temporary;
        [ReadOnly] public ComponentLookup<CarCurrentLane> CurrentLanes;
        [ReadOnly] public ComponentLookup<Controller> Controllers;
        [ReadOnly] public BufferLookup<CarNavigationLane> Navigation;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjects;
        [ReadOnly] public ComponentLookup<Updated> UpdatedLanes;
        /// <summary>Present on vehicles whose route is owned by a line or a dispatch trip.</summary>

        public ComponentLookup<PathOwner> PathOwners;
        public ComponentLookup<CarLane> Lanes;

        public NativeList<RoadLaneLease> Leases;
        public NativeList<EnforcementAttempt> Attempts;
        public NativeArray<uint> Counters;
        public NativeArray<uint> BudgetWindow;   // [0] mutations this window, [1] window start frame
        public EntityCommandBuffer Commands;

        public uint Frame;
        public int Revision;
        public bool ForceRelease;
        public uint LeaseLifetime;
        public uint AttemptDeadline;
        public uint MaxMutationsPerWindow;
        public bool EmergencyProtectionEnabled;

        private bool Valid(Entity entity) => entity != Entity.Null && Entities.Exists(entity) &&
                                             !Deleted.HasComponent(entity) && !Temporary.HasComponent(entity);

        public void Execute()
        {
            ReleaseExpired();
            AgeAttempts();

            if (ForceRelease) return;

            for (var i = 0; i < Evaluations.Length; i++)
            {
                var evaluation = Evaluations[i];
                if (evaluation.m_Verdict != RerouteSafetyVerdict.Safe)
                {
                    Counters[cRefusedNotSafe]++;
                    continue;
                }
                if (!TryRequest(evaluation)) continue;
            }
        }

        // Compare before restore. Any external change ends ownership, including an empty value.
        private void ReleaseExpired()
        {
            for (var i = Leases.Length - 1; i >= 0; i--)
            {
                var lease = Leases[i];
                if (!Valid(lease.Lane) || !Lanes.HasComponent(lease.Lane))
                {
                    lease.State = RoadLeaseState.LaneDeleted;
                    Leases[i] = lease;
                    Leases.RemoveAtSwapBack(i);
                    Counters[cLeasesLaneDeleted]++;
                    continue;
                }

                var expired = ForceRelease || RoadLeaseRules.HasExpired(Frame, lease.AbsoluteExpiryFrame) ||
                              lease.RestrictionRevision != Revision || !Valid(lease.Target);
                var lane = Lanes[lease.Lane];
                var owns = RoadLeaseRules.OwnsCurrent(lease, lane.m_BlockageStart, lane.m_BlockageEnd);

                if (expired)
                {
                    if (owns)
                    {
                        lane.m_BlockageStart = lease.OriginalBlockageStart;
                        lane.m_BlockageEnd = lease.OriginalBlockageEnd;
                        Lanes[lease.Lane] = lane;
                        Commands.AddComponent<Updated>(lease.Lane);
                        lease.State = RoadLeaseState.Released;
                        Counters[cLeasesReleased]++;
                    }
                    else if (Valid(lease.Lane))
                    {
                        lease.State = RoadLeaseState.RestoreConflict;
                        Counters[cLeaseRestoreConflict]++;
                    }
                    Leases[i] = lease;
                    Leases.RemoveAtSwapBack(i);
                    continue;
                }

                if (!owns)
                {
                    // Empty is also an external modification. Never re-assert a lost write.
                    Leases.RemoveAtSwapBack(i);
                    Counters[cLeaseRestoreConflict]++;
                }
            }
        }

        private void AgeAttempts()
        {
            for (var i = Attempts.Length - 1; i >= 0; i--)
            {
                var attempt = Attempts[i];
                var expired = RoadLeaseRules.HasExpired(Frame, attempt.AbsoluteDeadlineFrame);
                if (!Valid(attempt.Vehicle) || !Valid(attempt.Target) ||
                    attempt.RestrictionRevision != Revision || ForceRelease || expired ||
                    !CurrentLanes.TryGetComponent(attempt.Vehicle, out var current) ||
                    current.m_Lane != attempt.GateEntryLane)
                {
                    if (attempt.State == EnforcementAttemptState.Requested) Counters[cAttemptsUnresolved]++;
                    if (PathOwners.TryGetComponent(attempt.Vehicle, out var ownedPath) &&
                        EnforcementPolicy.OwnsRequest(attempt, ownedPath.m_State, ownedPath.m_ElementIndex))
                    {
                        ownedPath.m_State = attempt.OriginalPathState;
                        PathOwners[attempt.Vehicle] = ownedPath;
                    }
                    Attempts.RemoveAtSwapBack(i);
                    continue;
                }
                if (attempt.State != EnforcementAttemptState.Requested) continue;
                if (!PathOwners.TryGetComponent(attempt.Vehicle, out var owner) ||
                    (owner.m_State & (PathFlags.Failed | PathFlags.Stuck)) != 0)
                {
                    attempt.State = EnforcementAttemptState.Unresolved;
                    Counters[cAttemptsUnresolved]++;
                }
                else if ((owner.m_State & (PathFlags.Obsolete | PathFlags.DivertObsolete | PathFlags.Pending | PathFlags.Scheduled)) == 0)
                {
                    attempt.State = EnforcementAttemptState.Resolved;
                    Counters[cAttemptsResolved]++;
                }
                // Terminal records suppress another request for this approach, within a hard cap.
                Attempts[i] = attempt;
            }
        }

        private bool TryRequest(in RerouteSafetyEvaluation evaluation)
        {
            if (evaluation.m_Confidence != SafetyConfidence.Calibrated ||
                !IsCanonical(evaluation.m_PhysicalVehicle, evaluation.m_Vehicle) ||
                Frame - evaluation.m_EvaluationFrame > 1 ||
                !CurrentLanes.TryGetComponent(evaluation.m_Vehicle, out var currentLane) ||
                currentLane.m_Lane != evaluation.m_EntryLane ||
                currentLane.m_ChangeLane != Entity.Null || currentLane.m_ChangeProgress != 0 ||
                (currentLane.m_LaneFlags & (Game.Vehicles.CarLaneFlags.TransformTarget |
                    Game.Vehicles.CarLaneFlags.ParkingSpace | Game.Vehicles.CarLaneFlags.EnteringRoad |
                    Game.Vehicles.CarLaneFlags.Obsolete | Game.Vehicles.CarLaneFlags.Area)) != 0 ||
                !Navigation.TryGetBuffer(evaluation.m_Vehicle, out var navigation) || navigation.Length == 0 ||
                navigation[0].m_Lane != evaluation.m_NextLane) return false;
            if (evaluation.m_RestrictionRevision != Revision)
            {
                Counters[cRefusedStaleRevision]++;
                return false;
            }
            if (!Valid(evaluation.m_Vehicle) || !Valid(evaluation.m_Target))
            {
                Counters[cRefusedTopology]++;
                return false;
            }
            if (EmergencyProtectionEnabled && EnforcementPolicy.IsExempt(evaluation.m_Category))
            {
                Counters[cRefusedExempt]++;
                return false;
            }
            if (evaluation.m_Category == RoadVehicleCategory.PublicTransport)
            {
                // Fixed line, freight line or dispatch trip. Its route is redrawn by its own AI
                // every tick, so a reroute request would be undone immediately and a failure would
                // damage the whole line. Grandfather instead.
                Counters[cRefusedFixedRoute]++;
                return false;
            }
            if (!PathOwners.TryGetComponent(evaluation.m_Vehicle, out var owner))
            {
                Counters[cRefusedPathBusy]++;
                return false;
            }
            if (!EnforcementPolicy.CanRequestReroute(owner.m_State))
            {
                Counters[cRefusedPathBusy]++;
                return false;
            }
            if (FindAttempt(evaluation.m_Vehicle, evaluation.m_Target) >= 0)
            {
                // Exactly one attempt per vehicle and target, ever. No retry queue, no cooldown
                // ladder, no refcount.
                Counters[cRefusedAlreadyActive]++;
                return false;
            }
            if (!Valid(evaluation.m_NextLane) || !Lanes.HasComponent(evaluation.m_NextLane))
            {
                Counters[cRefusedTopology]++;
                return false;
            }

            // Never retry an old approach after a terminal record ages out.
            if (Frame - evaluation.m_FirstSeenFrame > 2 && FindLane(evaluation.m_NextLane) < 0) return false;
            var leaseIndex = FindLane(evaluation.m_NextLane);
            if (leaseIndex < 0)
            {
                if (Leases.Length >= kMaxLeases || Attempts.Length >= kMaxAttempts)
                {
                    Counters[cRefusedStoreFull]++;
                    Counters[cGrandfathered]++;
                    return false;
                }
                if (!ConsumeMutationBudget(2))
                {
                    Counters[cRefusedBudget]++;
                    Counters[cGrandfathered]++;
                    return false;
                }
                if (!TryAcquireLease(evaluation, out leaseIndex))
                {
                    Counters[cGrandfathered]++;
                    return false;
                }
            }

            if (Attempts.Length >= kMaxAttempts)
            {
                Counters[cRefusedStoreFull]++;
                Counters[cGrandfathered]++;
                return false;
            }

            var publicationLease = Leases[leaseIndex];
            if (publicationLease.Target != evaluation.m_Target || publicationLease.GateEntryLane != evaluation.m_EntryLane) return false;
            // A frame boundary alone is not acknowledgement: the native Updated marker must
            // be consumed and the owned interval must still exist. Otherwise grandfather.
            if (publicationLease.CreatedFrame == Frame || UpdatedLanes.HasComponent(publicationLease.Lane) ||
                !RoadLeaseRules.OwnsCurrent(publicationLease, Lanes[publicationLease.Lane].m_BlockageStart,
                    Lanes[publicationLease.Lane].m_BlockageEnd)) return false;
            var originalState = owner.m_State;
            owner.m_State |= PathFlags.Obsolete;
            PathOwners[evaluation.m_Vehicle] = owner;

            Attempts.Add(new EnforcementAttempt
            {
                Vehicle = evaluation.m_Vehicle,
                Target = evaluation.m_Target,
                GateEntryLane = evaluation.m_EntryLane,
                OwnedLane = Leases[leaseIndex].Lane,
                RestrictionRevision = Revision,
                OriginalPathState = originalState,
                WrittenPathState = owner.m_State,
                OriginalElementIndex = owner.m_ElementIndex,
                RequestedFrame = Frame,
                AbsoluteDeadlineFrame = Frame + AttemptDeadline,
                Backend = EnforcementBackend.Road,
                State = EnforcementAttemptState.Requested,
                LastRefusal = EnforcementRefusalReason.None,
                Category = evaluation.m_Category
            });
            Counters[cReroutesRequested]++;
            Counters[cAttemptsOpened]++;
            return true;
        }

        private bool IsCanonical(Entity physical, Entity expected)
        {
            var entity = physical;
            for (var depth = 0; depth < 4; depth++)
            {
                if (!Valid(entity)) return false;
                if (!Controllers.TryGetComponent(entity, out var controller) ||
                    controller.m_Controller == Entity.Null || controller.m_Controller == entity)
                    return entity == expected;
                entity = controller.m_Controller;
            }
            return false;
        }

        private bool TryAcquireLease(in RerouteSafetyEvaluation evaluation, out int leaseIndex)
        {
            leaseIndex = -1;
            if (LaneObjects.TryGetBuffer(evaluation.m_NextLane, out var occupants) && occupants.Length != 0)
            {
                Counters[cRefusedLaneOccupied]++;
                return false;
            }
            var lane = Lanes[evaluation.m_NextLane];
            if (!RoadLeaseRules.CanAcquire(lane.m_BlockageStart, lane.m_BlockageEnd))
            {
                Counters[cRefusedLaneOccupied]++;
                return false;
            }

            var index = Leases.Length;
            Leases.Add(new RoadLaneLease
            {
                Lane = evaluation.m_NextLane,
                Target = evaluation.m_Target,
                GateEntryLane = evaluation.m_EntryLane,
                RestrictionRevision = Revision,
                OriginalBlockageStart = lane.m_BlockageStart,
                OriginalBlockageEnd = lane.m_BlockageEnd,
                WrittenBlockageStart = 0,
                WrittenBlockageEnd = 1,
                CreatedFrame = Frame,
                AbsoluteExpiryFrame = Frame + LeaseLifetime,
                OwnerToken = ++BudgetWindow[2],
                State = RoadLeaseState.Active
            });

            lane.m_BlockageStart = 0;
            lane.m_BlockageEnd = 1;
            Lanes[evaluation.m_NextLane] = lane;
            Commands.AddComponent<Updated>(evaluation.m_NextLane);
            Counters[cLeasesAcquired]++;
            leaseIndex = index;
            return true;
        }

        private int FindLane(Entity lane)
        {
            for (var i = 0; i < Leases.Length; i++)
                if (Leases[i].Lane == lane) return i;
            return -1;
        }

        private int FindAttempt(Entity vehicle, Entity target)
        {
            for (var i = 0; i < Attempts.Length; i++)
                if (Attempts[i].Vehicle == vehicle && Attempts[i].Target == target) return i;
            return -1;
        }

        private bool ConsumeMutationBudget(uint cost)
        {
            if (Frame - BudgetWindow[1] >= kSecondInFrames)
            {
                BudgetWindow[1] = Frame;
                BudgetWindow[0] = 0;
            }
            if (BudgetWindow[0] + cost > MaxMutationsPerWindow) return false;
            BudgetWindow[0] += cost;
            return true;
        }
    }

    private NativeList<RoadLaneLease> m_Leases;
    private NativeList<EnforcementAttempt> m_Attempts;
    private NativeArray<uint> m_Counters;
    private NativeArray<uint> m_BudgetWindow;
    private RestrictionSafetySystem m_Safety;
    private RestrictionIndexSystem m_Index;
    private SimulationSystem m_Simulation;
    private EndFrameBarrier m_Barrier;
    private UpdateSystem m_Update;
    private JobHandle m_Work;

    public bool EnforcementEnabled { get; set; } = true;

    /// <summary>Active owned state. Used by Reset, save-time release and diagnostics.</summary>
    public int ActiveLeases { get; private set; }
    public int ActiveAttempts { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Leases = new NativeList<RoadLaneLease>(kMaxLeases, Allocator.Persistent);
        m_Attempts = new NativeList<EnforcementAttempt>(kMaxAttempts, Allocator.Persistent);
        m_Counters = new NativeArray<uint>(cCounterCount, Allocator.Persistent);
        m_BudgetWindow = new NativeArray<uint>(3, Allocator.Persistent);
        m_Safety = World.GetOrCreateSystemManaged<RestrictionSafetySystem>();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_Barrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
        m_Update = World.GetOrCreateSystemManaged<UpdateSystem>();
    }

    protected override void OnDestroy()
    {
        ReleaseAll();
        m_Leases.Dispose();
        m_Attempts.Dispose();
        m_Counters.Dispose();
        m_BudgetWindow.Dispose();
        base.OnDestroy();
    }

    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
    {
        ReleaseAll();
        base.OnGamePreload(purpose, mode);
    }

    protected override void OnUpdate()
    {
        // Save and unload boundary. CarLane is ISerializable and writes its blockage interval, so
        // a lease that is still active at serialization time would survive in the player's city.
        // This runs in the Serialize phase ahead of the game's SerializerSystem.
        if (m_Update.currentPhase == SystemUpdatePhase.Serialize)
        {
            ReleaseAll();
            return;
        }

        if (!m_Work.IsCompleted) return;
        m_Work.Complete();
        ActiveLeases = m_Leases.Length;
        ActiveAttempts = m_Attempts.Length;

        if (!EnforcementEnabled || Mod.Settings?.EnableRoadEnforcement == false ||
            !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable)
        {
            if (ActiveLeases == 0 && ActiveAttempts == 0) return;
            ReleaseAll();
            return;
        }

        if (!m_Work.IsCompleted) return;
        m_Work.Complete();

        if (m_Leases.Length == 0 && m_Attempts.Length == 0 &&
            (m_Index.ActiveTargetCount == 0 || m_Safety.EvaluationCount == 0)) return;

        var evaluations = m_Safety.GetEvaluations(out var safetyDependency);
        var job = new CoordinateJob
        {
            Evaluations = evaluations,
            Entities = GetEntityStorageInfoLookup(),
            Deleted = GetComponentLookup<Deleted>(true),
            Temporary = GetComponentLookup<Temp>(true),
            CurrentLanes = GetComponentLookup<CarCurrentLane>(true),
            Controllers = GetComponentLookup<Controller>(true),
            Navigation = GetBufferLookup<CarNavigationLane>(true),
            LaneObjects = GetBufferLookup<LaneObject>(true),
            UpdatedLanes = GetComponentLookup<Updated>(true),
            PathOwners = GetComponentLookup<PathOwner>(),
            Lanes = GetComponentLookup<CarLane>(),
            Leases = m_Leases,
            Attempts = m_Attempts,
            Counters = m_Counters,
            BudgetWindow = m_BudgetWindow,
            Commands = m_Barrier.CreateCommandBuffer(),
            Frame = m_Simulation.frameIndex,
            Revision = m_Index.Revision,
            ForceRelease = Mod.RestrictionsDirty,
            LeaseLifetime = kLeaseLifetimeFrames,
            AttemptDeadline = kAttemptDeadlineFrames,
            MaxMutationsPerWindow = kMaxGraphMutationsPerSecond,
            EmergencyProtectionEnabled = Mod.Settings?.EmergencyProtection ?? true
        };
        m_Work = job.Schedule(JobHandle.CombineDependencies(Dependency, safetyDependency));
        m_Safety.AddEvaluationReader(m_Work);
        m_Barrier.AddJobHandleForProducer(m_Work);
        Dependency = m_Work;
    }

    /// <summary>
    /// Synchronised release of every RouteFilter-owned lane mutation. This is the only place that
    /// restores a lane, and it only restores lanes whose current value is exactly what RouteFilter
    /// wrote. Anything else is left alone.
    /// </summary>
    public void ReleaseAll()
    {
        if (!m_Leases.IsCreated) return;
        m_Work.Complete();
        Dependency.Complete();
        for (var i = m_Leases.Length - 1; i >= 0; i--)
        {
            var lease = m_Leases[i];
            if (!EntityManager.Exists(lease.Lane) || !EntityManager.HasComponent<CarLane>(lease.Lane))
            {
                m_Counters[cLeasesLaneDeleted]++;
                continue;
            }
            var lane = EntityManager.GetComponentData<CarLane>(lease.Lane);
            if (!RoadLeaseRules.OwnsCurrent(lease, lane.m_BlockageStart, lane.m_BlockageEnd))
            {
                m_Counters[cLeaseRestoreConflict]++;
                continue;
            }
            lane.m_BlockageStart = lease.OriginalBlockageStart;
            lane.m_BlockageEnd = lease.OriginalBlockageEnd;
            EntityManager.SetComponentData(lease.Lane, lane);
            if (!EntityManager.HasComponent<Updated>(lease.Lane)) EntityManager.AddComponent<Updated>(lease.Lane);
            m_Counters[cLeasesReleased]++;
        }
        for (var i = 0; i < m_Attempts.Length; i++)
        {
            var attempt = m_Attempts[i];
            if (!EntityManager.Exists(attempt.Vehicle) || !EntityManager.HasComponent<PathOwner>(attempt.Vehicle)) continue;
            var owner = EntityManager.GetComponentData<PathOwner>(attempt.Vehicle);
            if (!EnforcementPolicy.OwnsRequest(attempt, owner.m_State, owner.m_ElementIndex)) continue;
            owner.m_State = attempt.OriginalPathState;
            EntityManager.SetComponentData(attempt.Vehicle, owner);
        }
        m_Leases.Clear();
        m_Attempts.Clear();
        ActiveLeases = 0;
        ActiveAttempts = 0;
        m_BudgetWindow[0] = 0;
    }

    public void ResetRuntimeState()
    {
        ReleaseAll();
        for (var i = 0; i < m_Counters.Length; i++) m_Counters[i] = 0;
    }

    /// <summary>Snapshot of the counters. Cheap: the job is only synchronised when already finished.</summary>
    public void CopyCounters(NativeArray<uint> destination)
    {
        if (!destination.IsCreated || destination.Length < cCounterCount) return;
        if (!m_Work.IsCompleted) return;
        m_Work.Complete();
        for (var i = 0; i < cCounterCount; i++) destination[i] = m_Counters[i];
    }
}
