using System;
using Colossal.Entities;
using Game.SceneFlow;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using RouteFilter.Components;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed class RoadRestrictionSignTooltipSystem : TooltipSystemBase
{
    private RestrictionToolSystem m_Tool;
    private ToolSystem m_Tools;
    private StringTooltip m_Tooltip;
    private Entity m_LastTarget;
    private int m_LastRevision = -1, m_LastVisualRevision = -1;
    private string m_LastLocale;
    protected override void OnCreate()
    {
        base.OnCreate(); m_Tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>(); m_Tools = World.GetOrCreateSystemManaged<ToolSystem>();
        m_Tooltip = new StringTooltip { path = "RouteFilter.Sign" };
    }
    protected override void OnUpdate()
    {
        var target = m_Tool.HoveredSignTarget;
        if (m_Tools.activeTool != m_Tool || target == Entity.Null || m_Tool.PointerOverUi || !EntityManager.Exists(target)) return;
        var index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        var visualRevision = World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.Revision ?? 0;
        var dictionary = GameManager.instance.localizationManager.activeDictionary;
        if (target != m_LastTarget || m_LastRevision != index.Revision || m_LastVisualRevision != visualRevision || m_LastLocale != dictionary.localeID)
        {
            m_LastTarget = target; m_LastRevision = index.Revision; m_LastVisualRevision = visualRevision; m_LastLocale = dictionary.localeID;
            var count = EntityManager.TryGetBuffer(target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets) ? assets.Length : 0;
            var entries = index.GetAppliedRoadEntries(target); int active = 0; foreach (var entry in entries) if (entry.Enabled) active++;
            dictionary.TryGetValue("RouteFilter.UI.MapAssets",out var assetLabel); dictionary.TryGetValue("RouteFilter.UI.EntryDirections",out var entryLabel);
            m_Tooltip.value = LocalizedString.Value($"RouteFilter · {count} {assetLabel ?? "assets"} · {active}/{entries.Count} {entryLabel ?? "restricted entries"}");
        }
        AddMouseTooltip(m_Tooltip);
    }
}
