using System;
using System.Collections.Generic;
using System.Linq;
namespace RouteFilter.Persistence;
internal static class VehiclePlateLabels
{
    internal static string[] Select<T>(IEnumerable<T> selected,IEnumerable<T> applicable,Func<T,string> category)
    {
        var chosen = new HashSet<T>(selected); var available = applicable.ToArray();
        if (available.Length > 0 && available.All(chosen.Contains)) return Array.Empty<string>();
        return chosen.Select(category).Where(label => !string.IsNullOrEmpty(label)).Distinct(StringComparer.Ordinal).OrderBy(label => label,StringComparer.Ordinal).ToArray();
    }
}
