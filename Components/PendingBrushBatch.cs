using System.Collections.Generic;

namespace RouteFilter.Persistence;

// Holds a stroke's frozen input and unique targets. It has no persistence or mutation API.
internal sealed class PendingBrushBatch<T>
{
    internal readonly HashSet<T> Targets = new();
    internal readonly List<T> Assets = new();
    internal bool Active { get; private set; }
    internal bool Clear { get; private set; }
    internal void Begin(bool clear, IEnumerable<T> assets) { Reset(); Active = true; Clear = clear; Assets.AddRange(assets); }
    internal bool Add(T target) => Active && Targets.Count < 4096 && Targets.Add(target);
    internal void Reset() { Active = false; Clear = false; Targets.Clear(); Assets.Clear(); }
}
