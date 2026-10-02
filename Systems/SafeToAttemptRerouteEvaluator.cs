using Game.Vehicles;
using RouteFilter.Components;
using Unity.Mathematics;

namespace RouteFilter.Systems;

/// <summary>Pure, Burst-compatible Phase 1C safety classifier.</summary>
public static class SafeToAttemptRerouteEvaluator
{
    // Decompiled CarNavigationSystem 1.6.0f1 uses 4/15 s when calling
    // VehicleUtils.GetBrakingDistance for road navigation.
    public const float VanillaRoadNavigationTimeStep = 4f / 15f;

    public static RerouteSafetyEvaluation Evaluate(in RerouteSafetyInput input)
    {
        var candidate = input.m_Candidate;
        var reasons = SafetyReason.None;

        if (!input.m_VehicleValid) reasons |= SafetyReason.VehicleInvalid;
        if (!input.m_TargetValid) reasons |= SafetyReason.TargetInvalid;
        if (candidate.m_RestrictionRevision != input.m_CurrentRestrictionRevision)
            reasons |= SafetyReason.RestrictionRevisionChanged;
        if (!input.m_CanonicalControllerValid) reasons |= SafetyReason.CanonicalControllerChanged;
        if (!input.m_HasPathOwner) reasons |= SafetyReason.PathOwnerMissing;

        var state = input.m_PathState;
        if ((state & Game.Pathfind.PathFlags.Pending) != 0) reasons |= SafetyReason.PathPending;
        if ((state & Game.Pathfind.PathFlags.Scheduled) != 0) reasons |= SafetyReason.PathScheduled;
        if ((state & Game.Pathfind.PathFlags.Failed) != 0) reasons |= SafetyReason.PathFailed;
        if ((state & Game.Pathfind.PathFlags.Stuck) != 0) reasons |= SafetyReason.PathStuck;
        if ((state & Game.Pathfind.PathFlags.Obsolete) != 0) reasons |= SafetyReason.PathObsolete;
        if ((state & Game.Pathfind.PathFlags.Append) != 0) reasons |= SafetyReason.PathAppend;
        if ((state & Game.Pathfind.PathFlags.Divert) != 0) reasons |= SafetyReason.PathDivert;
        if ((state & Game.Pathfind.PathFlags.DivertObsolete) != 0) reasons |= SafetyReason.PathDivertObsolete;
        if ((state & Game.Pathfind.PathFlags.CachedObsolete) != 0) reasons |= SafetyReason.PathCachedObsolete;

        if (!input.m_CurrentLaneMatches) reasons |= SafetyReason.CurrentLaneChanged;
        if (!input.m_ImmediateLaneMatches) reasons |= SafetyReason.ImmediateLaneChanged;
        if (!input.m_LaneChangeUnambiguous) reasons |= SafetyReason.LaneChangeAmbiguous;
        if (!input.m_TransitionSupported) reasons |= SafetyReason.UnsupportedTransition;
        if (!input.m_EntryDirectionMatches) reasons |= SafetyReason.EntryDirectionChanged;
        if (!input.m_NextDirectionMatches) reasons |= SafetyReason.NextDirectionChanged;
        if (input.m_AtOrPastGateAnchor) reasons |= SafetyReason.AtOrPastGateAnchor;
        if (!input.m_HasCurve || !math.isfinite(input.m_Speed) || input.m_Speed < 0 ||
            !math.isfinite(input.m_CurveLength) || input.m_CurveLength <= 0 ||
            !math.isfinite(input.m_DistanceToGateAnchor) ||
            !math.isfinite(input.m_Calibration.m_DistanceToLastSafeDecisionPoint))
            reasons |= SafetyReason.CurveUnavailable;
        if (!(input.m_Braking > 0f) || !math.isfinite(input.m_Braking))
            reasons |= SafetyReason.BrakingUnavailable;

        var calibration = input.m_Calibration;
        if (!calibration.m_HasLastSafeDecisionPoint)
            reasons |= SafetyReason.LastSafeDecisionPointUnknown;
        if ((calibration.m_Confidence != SafetyConfidence.Calibrated &&
             calibration.m_Confidence != SafetyConfidence.ConservativeInitial) ||
            !(calibration.m_ExpectedLatencySeconds >= 0f) ||
            !math.isfinite(calibration.m_ExpectedLatencySeconds))
            reasons |= SafetyReason.LatencyUncalibrated;
        if (!(calibration.m_VehicleGeometryMargin >= 0f) ||
            !math.isfinite(calibration.m_VehicleGeometryMargin))
            reasons |= SafetyReason.GeometryMarginUncalibrated;
        if (!(calibration.m_LaneChangeMargin >= 0f) ||
            !math.isfinite(calibration.m_LaneChangeMargin))
            reasons |= SafetyReason.LaneChangeMarginUncalibrated;
        if (!(calibration.m_UncertaintyMargin >= 0f) ||
            !math.isfinite(calibration.m_UncertaintyMargin))
            reasons |= SafetyReason.UncertaintyMarginUncalibrated;

        var brakingDistance = (input.m_Braking > 0f && math.isfinite(input.m_Braking))
            ? 0.5f * input.m_Speed * input.m_Speed / input.m_Braking +
              input.m_Speed * VanillaRoadNavigationTimeStep
            : float.NaN;
        var latencyDistance = input.m_Speed * calibration.m_ExpectedLatencySeconds;
        var requiredDistance = latencyDistance + brakingDistance +
                               calibration.m_VehicleGeometryMargin +
                               calibration.m_LaneChangeMargin +
                               calibration.m_UncertaintyMargin;

        const SafetyReason definiteUnsafe =
            SafetyReason.VehicleInvalid |
            SafetyReason.TargetInvalid |
            SafetyReason.PathPending |
            SafetyReason.PathScheduled |
            SafetyReason.PathFailed |
            SafetyReason.PathStuck |
            SafetyReason.PathObsolete |
            SafetyReason.PathAppend |
            SafetyReason.PathDivert |
            SafetyReason.PathDivertObsolete |
            SafetyReason.PathCachedObsolete |
            SafetyReason.LaneChangeAmbiguous |
            SafetyReason.UnsupportedTransition |
            SafetyReason.AtOrPastGateAnchor;

        const SafetyReason unknownContext =
            SafetyReason.RestrictionRevisionChanged |
            SafetyReason.CanonicalControllerChanged |
            SafetyReason.PathOwnerMissing |
            SafetyReason.CurrentLaneChanged |
            SafetyReason.ImmediateLaneChanged |
            SafetyReason.EntryDirectionChanged |
            SafetyReason.NextDirectionChanged |
            SafetyReason.CurveUnavailable |
            SafetyReason.BrakingUnavailable |
            SafetyReason.LastSafeDecisionPointUnknown |
            SafetyReason.LatencyUncalibrated |
            SafetyReason.GeometryMarginUncalibrated |
            SafetyReason.LaneChangeMarginUncalibrated |
            SafetyReason.UncertaintyMarginUncalibrated;

        RerouteSafetyVerdict verdict;
        if ((reasons & definiteUnsafe) != 0)
        {
            verdict = RerouteSafetyVerdict.Unsafe;
        }
        else if ((reasons & unknownContext) != 0)
        {
            verdict = RerouteSafetyVerdict.Unknown;
        }
        else if (!(calibration.m_DistanceToLastSafeDecisionPoint > requiredDistance))
        {
            reasons |= SafetyReason.InsufficientAvailableDistance;
            verdict = RerouteSafetyVerdict.Unsafe;
        }
        else
        {
            verdict = RerouteSafetyVerdict.Safe;
        }

        return new RerouteSafetyEvaluation
        {
            m_Vehicle = candidate.m_Vehicle,
            m_PhysicalVehicle = candidate.m_PhysicalVehicle,
            m_Target = candidate.m_Target,
            m_EntryLane = candidate.m_EntryLane,
            m_NextLane = candidate.m_NextLane,
            m_ViaLane = candidate.m_ViaLane,
            m_MatchedPrefab = candidate.m_MatchedPrefab,
            m_Verdict = verdict,
            m_Confidence = calibration.m_Confidence,
            m_Reasons = reasons,
            m_Category = input.m_Category,
            m_PathState = state,
            m_RestrictionRevision = input.m_CurrentRestrictionRevision,
            m_CandidateFrame = candidate.m_DetectionFrame,
            m_FirstSeenFrame = candidate.m_FirstSeenFrame,
            m_EvaluationFrame = input.m_EvaluationFrame,
            m_CandidatePipelineFrames = input.m_EvaluationFrame - candidate.m_DetectionFrame,
            m_ObservedApproachFrames = input.m_EvaluationFrame - candidate.m_FirstSeenFrame,
            m_SelectedSimulationSpeed = input.m_SelectedSimulationSpeed,
            m_SmoothSimulationSpeed = input.m_SmoothSimulationSpeed,
            m_Speed = input.m_Speed,
            m_Braking = input.m_Braking,
            m_VanillaTimeStep = VanillaRoadNavigationTimeStep,
            m_BrakingDistance = brakingDistance,
            m_CurveLength = input.m_CurveLength,
            m_DistanceToGateAnchor = input.m_DistanceToGateAnchor,
            m_DistanceToLastSafeDecisionPoint = calibration.m_HasLastSafeDecisionPoint
                ? calibration.m_DistanceToLastSafeDecisionPoint
                : float.NaN,
            m_ExpectedLatencySeconds = calibration.m_ExpectedLatencySeconds,
            m_ExpectedLatencyDistance = latencyDistance,
            m_VehicleGeometryLength = input.m_VehicleGeometryLength,
            m_VehicleGeometryMargin = calibration.m_VehicleGeometryMargin,
            m_LaneChangeMargin = calibration.m_LaneChangeMargin,
            m_UncertaintyMargin = calibration.m_UncertaintyMargin,
            m_RequiredDistance = requiredDistance,
            m_EstimatedSecondsToGate = input.m_Speed > 1e-4f
                ? input.m_DistanceToGateAnchor / input.m_Speed
                : float.PositiveInfinity
        };
    }
}
