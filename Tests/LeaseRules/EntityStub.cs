// Offline fixtures never load the game. This mirrors the values of the two Game/Unity types that
// the pure rules touch, transcribed from Cities: Skylines II 1.6.0f1:
//   Game.Pathfind.PathFlags  -> Game.Pathfind/PathFlags.cs
//   Unity.Entities.Entity    -> a two-field struct, which is all the pure rules read or compare
// If either type changes upstream, these files must be re-transcribed. That is precisely why the
// pure rules never depend on anything beyond these values.
using System;

namespace Unity.Entities;

public struct Entity : IEquatable<Entity>
{
    public int Index;
    public int Version;

    public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
    public override bool Equals(object obj) => obj is Entity other && Equals(other);
    public override int GetHashCode() => (Index * 397) ^ Version;
    public static bool operator ==(Entity left, Entity right) => left.Equals(right);
    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}
