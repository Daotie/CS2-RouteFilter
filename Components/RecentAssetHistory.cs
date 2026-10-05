using System;
using System.Collections.Generic;
using System.Linq;
namespace RouteFilter.Persistence;
internal static class RecentAssetHistory
{
    // Newest successful Apply batch first; stable ordinal identities break ties within a batch.
    // Missing catalog entries remain persisted and reappear if their assets return.
    internal static string[] AfterSuccessfulApply(IEnumerable<string> history,IEnumerable<string> forbidden)
    {
        var used=forbidden.Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        var batch=new HashSet<string>(used,StringComparer.Ordinal);
        return used.Concat(history.Where(id=>!batch.Contains(id))).Distinct(StringComparer.Ordinal).Take(64).ToArray();
    }
}
