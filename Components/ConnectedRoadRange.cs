using System;
using System.Collections.Generic;
namespace RouteFilter.Persistence;
// Minimum edge-count chain; caller supplies sorted neighbours. This edits road
// topology, independent of vehicle routing, lane direction and pathfinding.
internal static class ConnectedRoadRange
{
    internal static IReadOnlyList<T> Find<T>(T start,T end,Func<T,IEnumerable<T>> neighbours,int budget)
    {
        var comparer = EqualityComparer<T>.Default;
        var previous = new Dictionary<T,T>(); var visited = new HashSet<T> { start }; var queue = new List<T> { start }; var cursor = 0;
        while (cursor < queue.Count && visited.Count <= budget)
        {
            var item = queue[cursor++];
            if (comparer.Equals(item,end))
            {
                var result = new List<T> { item };
                while (!comparer.Equals(item,start)) { item = previous[item]; result.Add(item); }
                result.Reverse(); return result;
            }
            foreach (var next in neighbours(item)) if (visited.Add(next)) { previous[next] = item; queue.Add(next); }
        }
        return Array.Empty<T>();
    }
}
