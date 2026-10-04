using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.UI.Binding;
using Colossal.Entities;
using RouteFilter.Components;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RouteFilterUISystem
{
    private readonly HashSet<string> m_Favorites = new(StringComparer.Ordinal);
    private readonly List<string> m_Recent = new();
    private string[] m_Clipboard = Array.Empty<string>();
    private bool m_ClipboardReady;
    private ValueBinding<string> m_FavoriteIds, m_RecentIds;
    private ValueBinding<bool> m_HasClipboard;

    private static IEnumerable<string> ReadLibraryIds(string data)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith("1\n", StringComparison.Ordinal) || data.Length > 131072) yield break;
        foreach (var line in data.Substring(2).Split('\n').Take(512))
        {
            string value;
            try { value = Uri.UnescapeDataString(line); } catch { continue; }
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 512) yield return value;
        }
    }
    private static string WriteLibraryIds(IEnumerable<string> ids) => "1\n" + string.Join("\n", ids.Select(Uri.EscapeDataString));

    private void InitializeLibrary()
    {
        foreach (var id in ReadLibraryIds(Mod.Settings.FavoriteAssetIds)) m_Favorites.Add(id);
        m_Recent.AddRange(ReadLibraryIds(Mod.Settings.RecentAssetIds).Distinct(StringComparer.Ordinal).Take(64));
        m_FavoriteIds = CreateValue("favoriteAssetIds", string.Empty);
        m_RecentIds = CreateValue("recentAssetIds", string.Empty);
        m_HasClipboard = CreateValue("hasAssetClipboard", false);
        AddBinding(new TriggerBinding<int>(Mod.Id, "toggleFavoriteAsset", id =>
        {
            if (!m_AssetsById.TryGetValue(id, out var entity)) return;
            var name = m_PrefabSystem.GetPrefabName(entity);
            if (!m_Favorites.Remove(name) && m_Favorites.Count < 512) m_Favorites.Add(name);
            Mod.Settings.FavoriteAssetIds = WriteLibraryIds(m_Favorites.OrderBy(value => value, StringComparer.Ordinal));
            Mod.Settings.ApplyAndSave(); PublishLibrary();
        }));
        AddBinding(new TriggerBinding(Mod.Id, "copyAssetRestriction", () =>
        {
            var target = m_RestrictionTool.SelectedTarget;
            if (target == Entity.Null || !EntityManager.Exists(target)) return;
            m_Clipboard = EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets)
                ? assets.Where(asset => m_IdsByAsset.ContainsKey(asset.m_Prefab)).Select(asset => m_PrefabSystem.GetPrefabName(asset.m_Prefab)).Distinct(StringComparer.Ordinal).ToArray() : Array.Empty<string>();
            m_ClipboardReady = true; m_HasClipboard.Update(true);
        }));
        AddBinding(new TriggerBinding(Mod.Id, "pasteAssetRestriction", () =>
        {
            // Only the pending forbidden set changes. Directions, target and visuals stay intact.
            if (!m_ClipboardReady || !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable || m_RestrictionTool.SelectedTarget == Entity.Null) return;
            var names = new HashSet<string>(m_Clipboard, StringComparer.Ordinal);
            Mod.SelectedVehicleAssets.Clear();
            foreach (var asset in m_AssetsById.Values)
                if (m_ModeByAsset.TryGetValue(asset, out var mode) && (mode & m_RestrictionTool.SelectedTransportMode) != 0 &&
                    names.Contains(m_PrefabSystem.GetPrefabName(asset))) Mod.SelectedVehicleAssets.Add(asset);
            UpdateSelectedBinding(); PublishRestriction();
        }));
    }
    internal void RecordRecentApply()
    {
        var target = m_RestrictionTool.SelectedTarget;
        if (target == Entity.Null || !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable ||
            !EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets)) return;
        RecordRecentAssets(assets.Select(asset => asset.m_Prefab));
    }
    internal void RecordRecentAssets(IEnumerable<Entity> assets)
    {
        var used = assets.Where(m_IdsByAsset.ContainsKey).Select(m_PrefabSystem.GetPrefabName).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var ids = new HashSet<string>(used, StringComparer.Ordinal);
        m_Recent.RemoveAll(ids.Contains); m_Recent.InsertRange(0, used);
        if (m_Recent.Count > 64) m_Recent.RemoveRange(64, m_Recent.Count - 64);
        Mod.Settings.RecentAssetIds = WriteLibraryIds(m_Recent);
        Mod.Settings.ApplyAndSave(); PublishLibrary();
    }
    private void PublishLibrary()
    {
        var byName = m_AssetsById.GroupBy(pair => m_PrefabSystem.GetPrefabName(pair.Value), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
        m_FavoriteIds.Update(string.Join(",", m_Favorites.Where(byName.ContainsKey).Select(id => byName[id])));
        m_RecentIds.Update(string.Join(",", m_Recent.Where(byName.ContainsKey).Select(id => byName[id])));
    }
}
