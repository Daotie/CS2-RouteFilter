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
    private ValueBinding<string> m_LibraryFeedback;
    private ValueBinding<int> m_RecentRevisionBinding;
    private int m_LibraryFeedbackRevision,m_RecentRevision;
    private string m_RecentPublished="";

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
        m_LibraryFeedback=CreateValue("libraryFeedback","");
        m_RecentRevisionBinding=CreateValue("recentRevision",0);
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
            SynchronizeLibraryTarget();
            var target = m_RestrictionTool.SelectedTarget;
            if (target == Entity.Null || !EntityManager.Exists(target)) return;
            m_Clipboard = EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets)
                ? RouteFilter.Persistence.AssetLibrarySnapshot.Read(assets.Length, i => assets[i].m_Prefab)
                    .Where(m_IdsByAsset.ContainsKey).Select(m_PrefabSystem.GetPrefabName).Distinct(StringComparer.Ordinal).ToArray() : Array.Empty<string>();
            m_ClipboardReady = m_Clipboard.Length>0; m_HasClipboard.Update(m_ClipboardReady); LibraryFeedback("Copied",m_Clipboard.Length);
        }));
        AddBinding(new TriggerBinding(Mod.Id, "pasteAssetRestriction", () =>
        {
            SynchronizeLibraryTarget();
            // Only the pending forbidden set changes. Directions, target and visuals stay intact.
            if (!m_ClipboardReady || !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable || m_RestrictionTool.SelectedTarget == Entity.Null) return;
            var compatible=RouteFilter.Persistence.AssetLibrarySnapshot.Compatible(m_AssetsById.Values,
                m_Clipboard, m_RestrictionTool.SelectedTransportMode, m_PrefabSystem.GetPrefabName,
                asset => m_ModeByAsset.TryGetValue(asset, out var mode) ? mode : 0);
            if(compatible.Length==0) { LibraryFeedback("Incompatible",0); return; }
            Mod.SelectedVehicleAssets.Clear();
            foreach(var asset in compatible) Mod.SelectedVehicleAssets.Add(asset);
            UpdateSelectedBinding(); PublishRestriction(); LibraryFeedback("Pasted",compatible.Length);
            Mod.Log.Info($"[RouteFilter.Clipboard] pasted pending={compatible.Length} saved={m_Clipboard.Length}; directions unchanged; Apply required");
        }));
    }
    internal void RecordRecentApply()
    {
        var target = m_RestrictionTool.SelectedTarget;
        if (target == Entity.Null || !EntityManager.Exists(target) || EntityManager.HasComponent<Game.Common.Deleted>(target) || !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable ) return;
        if (!EntityManager.TryGetBuffer(target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets) || assets.Length==0) { LibraryFeedback("AllowAll",0); return; }
        RecordRecentAssets(RouteFilter.Persistence.AssetLibrarySnapshot.Read(assets.Length, i => assets[i].m_Prefab));
    }
    private void SynchronizeLibraryTarget()
    {
        var target = m_RestrictionTool.SelectedTarget;
        if (m_LastSelectedTarget == target) return;
        m_LastSelectedTarget = target;
        m_SelectedTargetBinding.Update(target == Entity.Null ? string.Empty : $"{target.Index}:{target.Version}");
        LoadSelectedTargetAssets(target);
    }
    internal void RecordRecentAssets(IEnumerable<Entity> assets)
    {
        var used = assets.Where(m_IdsByAsset.ContainsKey).Select(m_PrefabSystem.GetPrefabName).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var updated=RouteFilter.Persistence.RecentAssetHistory.AfterSuccessfulApply(m_Recent,used);
        m_Recent.Clear(); m_Recent.AddRange(updated);
        Mod.Settings.RecentAssetIds = WriteLibraryIds(m_Recent);
        Mod.Settings.ApplyAndSave(); PublishLibrary();
        m_RecentRevisionBinding.Update(++m_RecentRevision);
        LibraryFeedback("Applied",used.Length);
        Mod.Log.Info($"[RouteFilter.Recent] successfulApply assets={used.Length} stored={m_Recent.Count} revision={m_RecentRevision} binding={m_RecentPublished}");
    }
    private void LibraryFeedback(string kind,int count) => m_LibraryFeedback.Update(kind+"|"+count+"|"+(++m_LibraryFeedbackRevision));
    private void PublishLibrary()
    {
        var byName = m_AssetsById.GroupBy(pair => m_PrefabSystem.GetPrefabName(pair.Value), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
        m_FavoriteIds.Update(string.Join(",", m_Favorites.Where(byName.ContainsKey).Select(id => byName[id])));
        m_RecentPublished=string.Join(",", m_Recent.Where(byName.ContainsKey).Select(id => byName[id]));
        m_RecentIds.Update(m_RecentPublished);
    }
}
