// Pure lease rules never inspect entity identity. This fixture excludes Unity runtime.
namespace Unity.Entities;
public struct Entity { public int Index, Version; }
