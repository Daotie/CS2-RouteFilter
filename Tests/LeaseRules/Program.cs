using RouteFilter.Components;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
Check(LaneLeaseRules.IsEmpty(255, 0), "vanilla empty interval");
Check(!LaneLeaseRules.IsEmpty(0, 0), "0/0 must not be assumed empty");
Check(!LaneLeaseRules.IsEmpty(0, 255), "full blockage belongs to existing owner");
Check(!LaneLeaseRules.HasExpired(99, 100), "before expiry");
Check(LaneLeaseRules.HasExpired(100, 100), "absolute expiry inclusive");
Check(LaneLeaseRules.HasExpired(101, 100), "after expiry");
Check(!LaneLeaseRules.HasExpired(uint.MaxValue, 1), "frame wrap before expiry");
Check(LaneLeaseRules.HasExpired(1, 1), "frame wrap expiry");
var lease = new RouteFilterLaneLease {
    OriginalBlockageStart = 255, OriginalBlockageEnd = 0,
    WrittenBlockageStart = 0, WrittenBlockageEnd = 1,
    State = LaneLeaseState.Active
};
var exactMatches = 0;
for (var start = 0; start <= 255; start++)
for (var end = 0; end <= 255; end++)
{
    var owns = LaneLeaseRules.OwnsCurrent(lease, (byte)start, (byte)end);
    Check(owns == (start == 0 && end == 1), "must reject every conflicting pair");
    if (owns) exactMatches++;
}
Check(exactMatches == 1, "only exact written interval restored");
lease.State = LaneLeaseState.RestoreConflict;
Check(!LaneLeaseRules.OwnsCurrent(lease, 0, 1), "ended ownership cannot restore");
Check(lease.OriginalBlockageStart == 255 && lease.OriginalBlockageEnd == 0, "original state retained");
Console.WriteLine("PASS: 65,536 ownership pairs, vanilla empty interval, exact expiry, frame wrap, ended ownership. Unity jobs/save lifecycle NOT tested here.");
