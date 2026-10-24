using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Components;

internal static class RailConsistObservation
{
    // A carriage observed on another lane must not consume the front's scan slot.
    internal static bool TryClaim(Entity watchedLane, Entity frontLane, Entity consist,
        ref NativeParallelHashSet<Entity> seen)
        => watchedLane == frontLane && seen.Count() < 4096 && seen.Add(consist);
}
