using System;
using System.Collections.Generic;

namespace RouteFilter.Persistence;

// ECS DynamicBuffer implements IEnumerable, but the game's generic enumerator
// throws NotImplementedException. Snapshot by index before using managed LINQ.
internal static class AssetLibrarySnapshot
{
    internal static T[] Read<T>(int count, Func<int, T> read)
    {
        var result = new T[count];
        for (var i = 0; i < count; i++) result[i] = read(i);
        return result;
    }

    internal static T[] Compatible<T>(IEnumerable<T> catalog, IEnumerable<string> clipboard,
        int transport, Func<T, string> identity, Func<T, int> mode)
    {
        var names = new HashSet<string>(clipboard, StringComparer.Ordinal);
        var result = new List<T>();
        foreach (var asset in catalog)
            if ((mode(asset) & transport) != 0 && names.Contains(identity(asset))) result.Add(asset);
        return result.ToArray();
    }
}
