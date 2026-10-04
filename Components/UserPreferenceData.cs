using System;
using System.Collections.Generic;
using System.Linq;

namespace RouteFilter.Persistence;

// Settings data only: never contains entities, target geometry or direction intent.
internal sealed class UserPreset
{
    internal string Name;
    internal string[] Assets;
}

internal static class UserPreferenceData
{
    internal const int MaxPresets = 64, MaxAssets = 512;
    internal static List<UserPreset> Read(string data)
    {
        var result = new List<UserPreset>();
        if (string.IsNullOrEmpty(data) || data.Length > 2097152 || !data.StartsWith("1\n", StringComparison.Ordinal)) return result;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in data.Substring(2).Split('\n').Take(MaxPresets))
        {
            try
            {
                var parts = line.Split('|');
                var name = Uri.UnescapeDataString(parts[0]).Trim();
                if (name.Length == 0 || name.Length > 80 || !names.Add(name)) continue;
                var assets = parts.Skip(1).Take(MaxAssets).Select(Uri.UnescapeDataString)
                    .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 512).Distinct(StringComparer.Ordinal).ToArray();
                result.Add(new UserPreset { Name = name, Assets = assets });
            }
            catch (UriFormatException) { }
        }
        return result;
    }
    internal static string Write(IEnumerable<UserPreset> presets) => "1\n" + string.Join("\n", presets.Take(MaxPresets)
        .Select(preset => Uri.EscapeDataString(preset.Name) + "|" + string.Join("|", preset.Assets.Take(MaxAssets).Select(Uri.EscapeDataString))));
}
