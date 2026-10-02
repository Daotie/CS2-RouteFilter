using Game.Pathfind;
using RouteFilter.Components;
using RouteFilter.Systems;

var checks = 0;
void Check(bool result, string name) { checks++; if (!result) throw new Exception(name); }
RerouteSafetyInput Clean() => new()
{
    m_Candidate = new CandidateMatch { m_RestrictionRevision = 7, m_DetectionFrame = 10, m_FirstSeenFrame = 9 },
    m_CurrentRestrictionRevision = 7, m_EvaluationFrame = 11,
    m_VehicleValid = true, m_TargetValid = true, m_CanonicalControllerValid = true,
    m_HasPathOwner = true, m_CurrentLaneMatches = true, m_ImmediateLaneMatches = true,
    m_LaneChangeUnambiguous = true, m_TransitionSupported = true,
    m_EntryDirectionMatches = true, m_NextDirectionMatches = true, m_HasCurve = true,
    m_Speed = 15, m_Braking = 4, m_CurveLength = 400, m_DistanceToGateAnchor = 300,
    m_VehicleGeometryLength = 5,
    m_Calibration = new RerouteSafetyCalibration
    {
        m_Confidence = SafetyConfidence.ConservativeInitial, m_HasLastSafeDecisionPoint = true,
        m_DistanceToLastSafeDecisionPoint = 300, m_ExpectedLatencySeconds = 2.4f,
        m_VehicleGeometryMargin = 5, m_UncertaintyMargin = 5
    }
};
var input = Clean();
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Safe,
    "clean, distant candidate must bootstrap Safe without a measured calibration");
input.m_Calibration.m_Confidence = SafetyConfidence.Instrumenting;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unknown, "unbounded latency remains unknown");
input = Clean(); input.m_Calibration.m_DistanceToLastSafeDecisionPoint = 20;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unsafe, "late candidate passes without enforcement");
foreach (var flag in new[] { PathFlags.Pending, PathFlags.Scheduled, PathFlags.Failed, PathFlags.Stuck,
    PathFlags.Obsolete, PathFlags.Append, PathFlags.Divert, PathFlags.DivertObsolete, PathFlags.CachedObsolete })
{
    input = Clean(); input.m_PathState = flag;
    Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unsafe, "busy/unsupported path " + flag);
}
foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
{
    input = Clean(); input.m_Speed = invalid;
    Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict != RerouteSafetyVerdict.Safe, "invalid speed");
    input = Clean(); input.m_Braking = invalid;
    Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict != RerouteSafetyVerdict.Safe, "invalid braking");
}
input = Clean(); input.m_Calibration.m_DistanceToLastSafeDecisionPoint = float.NaN;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict != RerouteSafetyVerdict.Safe, "invalid live distance");
input = Clean(); input.m_ImmediateLaneMatches = false;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unknown, "uncertain navigation");
input = Clean(); input.m_CurrentRestrictionRevision++;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unknown, "stale restriction");
input = Clean(); input.m_AtOrPastGateAnchor = true;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unsafe, "already entered");
input = Clean(); input.m_LaneChangeUnambiguous = false;
Check(SafeToAttemptRerouteEvaluator.Evaluate(input).m_Verdict == RerouteSafetyVerdict.Unsafe, "lane change");
Console.WriteLine($"PASS: {checks} safety admission checks. Native queue and vehicle behavior NOT GAME VERIFIED.");
