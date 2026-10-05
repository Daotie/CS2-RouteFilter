using System;
using System.Linq;
using System.Collections.Generic;
using Colossal.UI.Binding;
using RouteFilter.Persistence;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RouteFilterUISystem
{
    private List<UserPreset> m_Presets;
    private ValueBinding<string> m_PresetCatalog;
    private ValueBinding<int> m_PresetMissing, m_PresetUnsupported;
    private void InitializePresets()
    {
        m_Presets = UserPreferenceData.Read(Mod.Settings.UserPresets);
        m_PresetCatalog = CreateValue("userPresets", string.Empty);
        m_PresetMissing = CreateValue("presetMissing", 0);
        m_PresetUnsupported = CreateValue("presetUnsupported", 0);
        AddBinding(new TriggerBinding<string,string>(Mod.Id,"renameUserPreset",(oldName,newName) =>
        {
            newName = newName.Trim();
            var preset = m_Presets.Find(item => item.Name == oldName);
            if (preset == null || newName.Length == 0 || newName.Length > 80 || m_Presets.Any(item => item != preset && item.Name == newName)) return;
            preset.Name = newName; SavePresets();
        }));
        AddBinding(new TriggerBinding<string>(Mod.Id,"loadBuiltinPreset",name =>
        {
            if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable || (name != "Trucks" && name != "CargoTrucks")) return;
            Mod.SelectedVehicleAssets.Clear();
            foreach (var asset in RoadVehicleAssets)
            {
                var cargo = EntityManager.HasComponent<Game.Prefabs.DeliveryTruckData>(asset) || EntityManager.HasComponent<Game.Prefabs.CargoTransportVehicleData>(asset);
                var truck = cargo || EntityManager.HasComponent<Game.Prefabs.GarbageTruckData>(asset) || EntityManager.HasComponent<Game.Prefabs.FireEngineData>(asset);
                if (name == "CargoTrucks" ? cargo : truck) Mod.SelectedVehicleAssets.Add(asset);
            }
            m_PresetMissing.Update(0); m_PresetUnsupported.Update(0); UpdateSelectedBinding(); PublishRestriction(); LibraryFeedback("Preset",Mod.SelectedVehicleAssets.Count);
        }));
        AddBinding(new TriggerBinding<string>(Mod.Id, "saveUserPreset", name =>
        {
            name = name.Trim();
            if (name.Length == 0 || name.Length > 80) return;
            var existing = m_Presets.Find(preset => preset.Name == name);
            if (existing == null)
            {
                if (m_Presets.Count >= UserPreferenceData.MaxPresets) return;
                m_Presets.Add(existing = new UserPreset { Name = name });
            }
            existing.Assets = Mod.SelectedVehicleAssets.Where(m_IdsByAsset.ContainsKey)
                .Select(m_PrefabSystem.GetPrefabName).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            SavePresets();
        }));
        AddBinding(new TriggerBinding<string>(Mod.Id, "deleteUserPreset", name => { m_Presets.RemoveAll(preset => preset.Name == name); SavePresets(); }));
        AddBinding(new TriggerBinding<string>(Mod.Id, "loadUserPreset", name =>
        {
            if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return;
            var preset = m_Presets.Find(item => item.Name == name);
            if (preset == null) return;
            var byName = m_AssetsById.Values.GroupBy(m_PrefabSystem.GetPrefabName,StringComparer.Ordinal).ToDictionary(group => group.Key,group => group.First(),StringComparer.Ordinal);
            var mode = m_RestrictionTool.SelectedTarget == Entity.Null ? 3 : m_RestrictionTool.SelectedTransportMode;
            int missing = 0, unsupported = 0;
            Mod.SelectedVehicleAssets.Clear();
            foreach (var id in preset.Assets)
            {
                if (!byName.TryGetValue(id, out var asset)) { missing++; continue; }
                if (!m_ModeByAsset.TryGetValue(asset, out var assetMode) || (assetMode & mode) == 0) { unsupported++; continue; }
                Mod.SelectedVehicleAssets.Add(asset);
            }
            m_PresetMissing.Update(missing); m_PresetUnsupported.Update(unsupported);
            UpdateSelectedBinding(); PublishRestriction(); LibraryFeedback("Preset",Mod.SelectedVehicleAssets.Count);
        }));
        PublishPresets();
    }
    private void SavePresets() { Mod.Settings.UserPresets = UserPreferenceData.Write(m_Presets); Mod.Settings.ApplyAndSave(); PublishPresets(); }
    private void PublishPresets() => m_PresetCatalog.Update(string.Join("\n", m_Presets.Select(preset => Uri.EscapeDataString(preset.Name))));
}
