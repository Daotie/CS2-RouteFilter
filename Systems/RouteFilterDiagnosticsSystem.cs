using Game;
using Game.Simulation;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Observational only. Aggregates the counters the pipeline already produces into one
/// rate-limited line per interval, and exposes the same numbers to the panel.
///
/// WORKLOAD GROWS WITH: number of backends (two), plus active runtime state. Never with traffic,
/// vehicles, lanes or targets.
/// FAST PATH: nothing is sampled until RouteFilter has work to report.
/// COMPLEXITY: O(1) per frame; a handful of integer reads and one string build per interval.
/// ALLOCATIONS: one string per interval when logging is enabled, zero when it is not.
/// JOB DEPENDENCIES: none. Every counter read is already non-synchronising.
/// MAIN THREAD SYNC: none. This system never calls JobHandle.Complete for any reason.
/// STRUCTURAL CHANGES: none.
/// </summary>
/// <remarks>
/// The point of this system is that "it is fast" and "RouteFilter is the cause" are both
/// falsifiable. Every field is a work count, so a frame-time difference can be attributed to a
/// concrete quantity rather than to a feeling.
/// </remarks>
public sealed partial class RouteFilterDiagnosticsSystem : GameSystemBase
{
    private const int kReportIntervalFrames = 300;
    private const int kCounterSlots = 21;

    private RoadEnforcementCoordinator m_Road = null!;
    private RailEnforcementBackend m_Rail = null!;
    private RestrictionCandidateSystem m_Candidate = null!;
    private RestrictionSafetySystem m_Safety = null!;
    private RestrictionIndexSystem m_Index = null!;
    private RestrictionPersistenceSystem m_Persistence = null!;
    private SimulationSystem m_Simulation = null!;

    private NativeArray<uint> m_RoadCounters;
    private NativeArray<uint> m_RailCounters;
    private uint[] m_RoadPrevious = new uint[kCounterSlots];
    private uint[] m_RailPrevious = new uint[kCounterSlots];

    private int m_WindowStartFrame;

    /// <summary>One aggregated window. Read by the panel and by the periodic log line.</summary>
    public DiagnosticsWindow LastWindow { get; private set; }

    public struct DiagnosticsWindow
    {
        public int StartFrame;
        public uint Frames;
        public int ActiveTargets;
        public int WatchedRoadEntryLanes;
        public int WatchedRoadGates;
        public int RoadLaneObjectsScanned;
        public int RoadPhysicalEntitiesSeen;
        public int RoadCandidatesMatched;
        public int RoadDuplicatesRemoved;
        public int RoadEmergencyExempt;
        public int RoadEvaluations;
        public int RoadSafe;
        public int RoadUnsafe;
        public int RoadUnknown;
        public int RoadReroutesRequested;
        public int RoadLeasesAcquired;
        public int RoadLeasesReleased;
        public int RoadLeasesReasserted;
        public int RoadGraphMutations;
        public int RoadRefusedBudget;
        public int RoadGrandfathered;
        public int RoadConfirmedNoAlternative;
        public int ActiveRoadLeases;
        public int ActiveRoadAttempts;
        public int RailWatchedEntryLanes;
        public int RailLaneObjectsScanned;
        public int RailCandidates;
        public int RailReroutesRequested;
        public int RailRefusedFixedRoute;
        public int RailRefusedNotSafe;
        public int RailConfirmedNoAlternative;
        public int ActiveRailAttempts;
        public bool PersistenceLocked;
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Road = World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>();
        m_Rail = World.GetOrCreateSystemManaged<RailEnforcementBackend>();
        m_Candidate = World.GetOrCreateSystemManaged<RestrictionCandidateSystem>();
        m_Safety = World.GetOrCreateSystemManaged<RestrictionSafetySystem>();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_Persistence = World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>();
        m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_RoadCounters = new NativeArray<uint>(kCounterSlots, Allocator.Persistent);
        m_RailCounters = new NativeArray<uint>(kCounterSlots, Allocator.Persistent);
        m_WindowStartFrame = (int)m_Simulation.frameIndex;
    }

    protected override void OnDestroy()
    {
        m_RoadCounters.Dispose();
        m_RailCounters.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (Mod.ConsumeNativeProtocolRequest())
        {
            try
            {
                RestrictionNativeProtocolFixtures.Run();
                Mod.Log.Info($"[RouteFilter.NativeProtocol] PASS build={Mod.BuildId}; isolated buffers only; city save/load remains unverified");
            }
            catch (System.Exception error)
            {
                Mod.Log.Error($"[RouteFilter.NativeProtocol] FAILED build={Mod.BuildId}: {error}");
            }
        }
        var frame = m_Simulation.frameIndex;
        var requested = Mod.ConsumeDiagnosticsRequest();
        var elapsed = unchecked((int)(frame - m_WindowStartFrame));
        if (!requested && (m_Index.ActiveTargetCount == 0 || Mod.Settings?.VerboseDiagnostics != true)) return;
        if (!requested && (uint)elapsed < kReportIntervalFrames) return;

        LastWindow = Sample(frame, elapsed <= 0 ? 1u : (uint)elapsed);
        m_WindowStartFrame = (int)frame;

        // Logging is opt-in and rate-limited. A release build with verbose diagnostics off produces
        // no strings at all, so the sampling cost is two integer array reads per interval.
        if (requested || Mod.Settings?.VerboseDiagnostics == true)
            Mod.Log.Info(Format(LastWindow));
    }

    private DiagnosticsWindow Sample(uint frame, uint frames)
    {
        m_Road.CopyCounters(m_RoadCounters);
        m_Rail.CopyCounters(m_RailCounters);

        var safety = m_Safety.GetLastCounters();
        var window = new DiagnosticsWindow
        {
            StartFrame = (int)frame,
            Frames = frames,
            ActiveTargets = m_Index.ActiveTargetCount,
            WatchedRoadEntryLanes = m_Candidate.WatchedEntryLaneCount,
            WatchedRoadGates = m_Candidate.WatchedGateCount,
            RoadEvaluations = safety.m_Evaluated,
            RoadSafe = safety.m_Safe,
            RoadUnsafe = safety.m_Unsafe,
            RoadUnknown = safety.m_Unknown,
            RoadCandidatesMatched = safety.m_Evaluated,
            RoadReroutesRequested = Delta(m_RoadCounters, m_RoadPrevious, 5),
            RoadLeasesAcquired = Delta(m_RoadCounters, m_RoadPrevious, 0),
            RoadLeasesReleased = Delta(m_RoadCounters, m_RoadPrevious, 1),
            RoadLeasesReasserted = Delta(m_RoadCounters, m_RoadPrevious, 2),
            RoadRefusedBudget = Delta(m_RoadCounters, m_RoadPrevious, 15),
            RoadGrandfathered = Delta(m_RoadCounters, m_RoadPrevious, 19),
            RoadConfirmedNoAlternative = Delta(m_RoadCounters, m_RoadPrevious, 20),
            ActiveRoadLeases = m_Road.ActiveLeases,
            ActiveRoadAttempts = m_Road.ActiveAttempts,
            RailWatchedEntryLanes = m_Rail.WatchedEntryLanes,
            RailLaneObjectsScanned = m_Rail.LaneObjectsScanned,
            RailCandidates = m_Rail.LastCandidateCount,
            RailReroutesRequested = Delta(m_RailCounters, m_RailPrevious, 5),
            RailRefusedFixedRoute = Delta(m_RailCounters, m_RailPrevious, 13),
            RailRefusedNotSafe = Delta(m_RailCounters, m_RailPrevious, 9),
            RailConfirmedNoAlternative = Delta(m_RailCounters, m_RailPrevious, 20),
            ActiveRailAttempts = m_Rail.ActiveAttempts,
            PersistenceLocked = m_Persistence.PersistenceLocked
        };

        // Every lane write that publishes into the pathfind graph is one mutation; acquire,
        // re-assert and release each cost one.
        window.RoadGraphMutations = window.RoadLeasesAcquired + window.RoadLeasesReasserted +
                                    window.RoadLeasesReleased;
        return window;
    }

    private static int Delta(NativeArray<uint> current, uint[] previous, int slot)
    {
        var value = current[slot];
        var delta = value >= previous[slot] ? value - previous[slot] : value;
        previous[slot] = value;
        return delta > int.MaxValue ? int.MaxValue : (int)delta;
    }

    private static string Format(in DiagnosticsWindow value)
        => $"[RouteFilter.Stats] frames={value.Frames} targets={value.ActiveTargets} " +
           $"roadWatchedLanes={value.WatchedRoadEntryLanes} roadGates={value.WatchedRoadGates} " +
           $"roadEvaluations={value.RoadEvaluations} roadSafe={value.RoadSafe} roadUnsafe={value.RoadUnsafe} " +
           $"roadUnknown={value.RoadUnknown} roadReroutes={value.RoadReroutesRequested} " +
           $"roadLeases={value.RoadLeasesAcquired}/{value.RoadLeasesReleased}/{value.RoadLeasesReasserted} " +
           $"roadGraphMutations={value.RoadGraphMutations} roadRefusedBudget={value.RoadRefusedBudget} " +
           $"roadGrandfathered={value.RoadGrandfathered} roadConfirmedNoAlternative={value.RoadConfirmedNoAlternative} roadActiveLeases={value.ActiveRoadLeases} " +
           $"roadActiveAttempts={value.ActiveRoadAttempts} " +
           $"railWatchedLanes={value.RailWatchedEntryLanes} railLaneObjects={value.RailLaneObjectsScanned} " +
           $"railReroutes={value.RailReroutesRequested} railRefusedFixedRoute={value.RailRefusedFixedRoute} " +
           $"railRefusedTooLate={value.RailRefusedNotSafe} railConfirmedNoAlternative={value.RailConfirmedNoAlternative} railActiveAttempts={value.ActiveRailAttempts} " +
           $"persistenceLocked={value.PersistenceLocked} build={Mod.BuildId} " +
           $"queryHook={RestrictionPathfindHook.Available} " +
           $"excludedQueries={RestrictionPathfindHook.Queries} roadQueries={RestrictionPathfindHook.RoadQueries} " +
           $"railQueries={RestrictionPathfindHook.RailQueries} alternatives={RestrictionPathfindHook.Alternatives} " +
           $"fallbacks={RestrictionPathfindHook.Fallbacks} roadAlternatives={RestrictionPathfindHook.RoadAlternatives} " +
           $"railAlternatives={RestrictionPathfindHook.RailAlternatives} roadFallbacks={RestrictionPathfindHook.RoadFallbacks} " +
           $"railFallbacks={RestrictionPathfindHook.RailFallbacks} appliedEdges={RestrictionPathfindHook.Applied} " +
           $"restoredEdges={RestrictionPathfindHook.Restored} restoreConflicts={RestrictionPathfindHook.Conflicts} " +
           $"activeQueries={RestrictionPathfindHook.ActiveTransactions}";
}
