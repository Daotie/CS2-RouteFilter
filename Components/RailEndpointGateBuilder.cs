using System.Collections.Generic;

namespace RouteFilter.Components;

// Builds the node-side approaches of a segment without creating node restrictions.
// Only paths whose next segment is the restricted segment are returned.
internal static class RailEndpointGateBuilder<TLane, TNode>
{
    internal readonly struct Traversal
    {
        internal readonly TLane Lane;
        internal readonly TNode Start, End;
        internal readonly bool TwoWay;
        internal Traversal(TLane lane, TNode start, TNode end, bool twoWay)
        { Lane = lane; Start = start; End = end; TwoWay = twoWay; }
    }
    internal readonly struct Gate
    {
        internal readonly TLane Entry, Next, Via;
        internal Gate(TLane entry, TLane next, TLane via = default)
        { Entry = entry; Next = next; Via = via; }
    }

    private static IEnumerable<Traversal> Directions(Traversal lane)
    {
        yield return lane;
        if (lane.TwoWay) yield return new Traversal(lane.Lane, lane.End, lane.Start, false);
    }

    internal static List<Gate> Build(IReadOnlyList<Traversal> restricted,
        IReadOnlyList<Traversal> connectors, IReadOnlyList<Traversal> approaches)
    {
        var result = new List<Gate>();
        var unique = new HashSet<Gate>();
        var nodes = EqualityComparer<TNode>.Default;
        var lanes = EqualityComparer<TLane>.Default;
        void Add(TLane entry, TLane next, TLane via = default)
        {
            var gate = new Gate(entry, next, via);
            if (!lanes.Equals(entry, next) && unique.Add(gate)) result.Add(gate);
        }
        foreach (var targetLane in restricted)
        foreach (var target in Directions(targetLane))
        {
            foreach (var connectorLane in connectors)
            foreach (var connector in Directions(connectorLane))
                if (nodes.Equals(connector.End, target.Start)) Add(connector.Lane, target.Lane);

            foreach (var approachLane in approaches)
            foreach (var approach in Directions(approachLane))
            {
                // Some native track nodes have no connector SubLane at all.
                if (nodes.Equals(approach.End, target.Start)) Add(approach.Lane, target.Lane);
                foreach (var connectorLane in connectors)
                foreach (var connector in Directions(connectorLane))
                    if (nodes.Equals(approach.End, connector.Start) && nodes.Equals(connector.End, target.Start))
                    {
                        Add(approach.Lane, target.Lane, connector.Lane);
                        // Native navigation may expose only the imminent connector.
                        // Use the node-style gate early only when its exit cannot lead
                        // to another adjacent segment. Ambiguous joins retain two-hop validation.
                        bool dedicated = true;
                        foreach (var otherLane in approaches)
                        foreach (var other in Directions(otherLane))
                            if (nodes.Equals(connector.End, other.Start)) dedicated = false;
                        if (dedicated) Add(approach.Lane, connector.Lane);
                    }
            }
        }
        return result;
    }
}
