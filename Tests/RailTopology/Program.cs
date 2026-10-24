using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using Builder = RouteFilter.Components.RailEndpointGateBuilder<int, int>;

int checks = 0;
void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
Builder.Traversal Lane(int id, int start, int end, bool twoWay = false) => new(id, start, end, twoWay);
var empty = Array.Empty<Builder.Traversal>();
bool Has(List<Builder.Gate> gates, int entry, int next, int via = 0)
    => gates.Any(g => g.Entry == entry && g.Next == next && g.Via == via);

// Direct native joins have no node-owned connector lane. Both segment endpoints work.
var target = new[] { Lane(10, 100, 200, true) };
var start = Builder.Build(target, empty, new[] { Lane(1, 0, 100) });
var end = Builder.Build(target, empty, new[] { Lane(2, 300, 200) });
Check(start.Count == 1 && Has(start, 1, 10), "direct start endpoint is watched without connector SubLanes");
Check(end.Count == 1 && Has(end, 2, 10), "reverse entry at end of bidirectional restricted track is watched");
var outbound = Builder.Build(new[] { Lane(10, 100, 200) }, empty, new[] { Lane(2, 300, 200) });
Check(outbound.Count == 0, "one-way track exit cannot become an entry gate");
Check(Builder.Build(target, empty, new[] { Lane(3, 400, 500, true) }).Count == 0,
    "disconnected parallel track is not restricted");
var reversedApproach = Builder.Build(target, empty, new[] { Lane(1, 100, 0, true) });
Check(Has(reversedApproach, 1, 10), "reversible upstream track can approach on its stored start");
Check(Builder.Build(target, empty, new[] { Lane(1, 100, 0) }).Count == 0,
    "one-way upstream departure is not mistaken for approach");

// Node-style lookahead is internal only: approach -> connector -> selected segment.
var connected = Builder.Build(target, new[] { Lane(5, 90, 100) }, new[] { Lane(1, 0, 90) });
Check(connected.Count == 3 && Has(connected, 5, 10) && Has(connected, 1, 10, 5) && Has(connected, 1, 5),
    "connector and early upstream watches both lead to the original segment");
var reverseConnector = Builder.Build(target, new[] { Lane(5, 100, 90, true) }, new[] { Lane(1, 0, 90) });
Check(Has(reverseConnector, 1, 10, 5), "reverse connector traversal preserves early detection");
var wrongDirection = Builder.Build(target, new[] { Lane(5, 100, 90) }, new[] { Lane(1, 0, 90) });
Check(wrongDirection.Count == 0, "cannot traverse a one-way connector backwards");
var wrongEnd = Builder.Build(target, new[] { Lane(5, 90, 100, true) }, new[] { Lane(1, 0, 100) });
Check(!Has(wrongEnd, 1, 10, 5), "shared endpoint cannot imply a U-turn inside a connector");
var junction = Builder.Build(target, new[] { Lane(5, 90, 100), Lane(6, 90, 500) },
    new[] { Lane(1, 0, 90), Lane(2, 500, 600) });
Check(junction.All(g => g.Next == 10 || g.Next == 5) && !junction.Any(g => g.Via == 6 || g.Entry == 2),
    "endpoint policy never expands to other exits or side branches of the node");
var repeated = Builder.Build(target.Concat(target).ToArray(), new[] { Lane(5, 90, 100), Lane(5, 90, 100) },
    new[] { Lane(1, 0, 90), Lane(1, 0, 90) });
Check(repeated.Count == 3, "duplicate topology entries cannot multiply gate candidates");
var sharedExit = Builder.Build(target, new[] { Lane(5, 90, 100) },
    new[] { Lane(1, 0, 90), Lane(2, 100, 500) });
Check(!Has(sharedExit, 1, 5) && Has(sharedExit, 1, 10, 5),
    "connector shared with a legal side exit requires explicit next-segment navigation");
Check(Has(connected, 1, 5), "dedicated connector is sufficient when navigation lookahead contains only one lane");
Check(Builder.Build(empty, new[] { Lane(5, 90, 100) }, new[] { Lane(1, 0, 90) }).Count == 0,
    "removing the segment restriction leaves no hidden endpoint gates");

// A long consist spans several watched lanes. Tail-first iteration must not hide its front.
foreach (var tailFirst in new[] { false, true })
{
    var seen = new NativeParallelHashSet<Entity>();
    var train = new Entity(10); var frontLane = new Entity(2); int admitted = 0;
    foreach (var lane in tailFirst ? new[] { new Entity(1), frontLane, frontLane } : new[] { frontLane, new Entity(1), frontLane })
        if (RailConsistObservation.TryClaim(lane, frontLane, train, ref seen)) admitted++;
    Check(admitted == 1, "carriage/front iteration order yields exactly one physical-front candidate");
}
var unclaimed = new NativeParallelHashSet<Entity>();
Check(!RailConsistObservation.TryClaim(new Entity(1), new Entity(2), new Entity(50), ref unclaimed) && unclaimed.Count() == 0,
    "carriage on another watched lane consumes no consist slot");
Check(RailConsistObservation.TryClaim(new Entity(2), new Entity(2), new Entity(50), ref unclaimed),
    "front stays eligible after its carriage was observed elsewhere");
Check(!RailConsistObservation.TryClaim(new Entity(2), new Entity(2), new Entity(50), ref unclaimed),
    "repeated members on front lane cannot duplicate reroutes");
for (int i = 0; i < 4095; i++)
    Check(RailConsistObservation.TryClaim(new Entity(2), new Entity(2), new Entity(100 + i), ref unclaimed), "bounded distinct consist admission");
Check(!RailConsistObservation.TryClaim(new Entity(2), new Entity(2), new Entity(10000), ref unclaimed) && unclaimed.Count() == 4096,
    "scan remains bounded at 4096 consists");
Console.WriteLine($"PASS: {checks} rail endpoint topology and consist scan checks. Live game navigation still requires verification.");
