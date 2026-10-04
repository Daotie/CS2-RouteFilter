// Transcribed from Cities: Skylines II 1.6.0f1, Game.Pathfind/PathFlags.cs.
namespace Game.Pathfind;

[System.Flags]
public enum PathFlags : ushort
{
    Pending = 1,
    Failed = 2,
    Obsolete = 4,
    Scheduled = 8,
    Append = 0x10,
    Updated = 0x20,
    Stuck = 0x40,
    WantsEvent = 0x80,
    AddDestination = 0x100,
    Debug = 0x200,
    Divert = 0x400,
    DivertObsolete = 0x800,
    CachedObsolete = 0x1000
}
