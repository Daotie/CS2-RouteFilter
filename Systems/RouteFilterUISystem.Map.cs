using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game.Common;
using Game.Net;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilter.Systems;

public sealed partial class RouteFilterUISystem
{
    private bool m_MapOpen, m_MapDirty = true;
    private ValueBinding<string> m_MapSnapshot;
    private ValueBinding<bool> m_BrushBinding;
    private ValueBinding<int> m_BrushPendingBinding;
    private EntityQuery m_MapRoads, m_MapTargets, m_MapChanged;
    private int m_MapVisualRevision = -1, m_MapIndexRevision = -1;
    private readonly Dictionary<string, Entity> m_MapSelection = new(StringComparer.Ordinal);
    private readonly Dictionary<Entity,uint> m_MapRoadStamps = new();
    private void InitializeMapAndBrush()
    {
        m_MapSnapshot = CreateValue("restrictionMap", string.Empty);
        m_BrushBinding = CreateValue("segmentBrush", false);
        m_BrushPendingBinding = CreateValue("brushPending", 0);
        m_MapRoads = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
            None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() } });
        m_MapTargets = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_MapChanged = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Edge>() },
            Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() } });
        AddBinding(new TriggerBinding<bool>(Mod.Id,"setRestrictionMapOpen", open => { m_MapOpen = open; m_MapDirty = true; if (!open) ClearMap(); }));
        AddBinding(new TriggerBinding<string>(Mod.Id,"selectMapTarget", key =>
        {
            if (!m_MapOpen || !m_MapSelection.TryGetValue(key,out var target) || !EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target)) return;
            m_RestrictionTool.SetBrushEnabled(false);
            Mod.SelectedTargetMode = EntityManager.HasComponent<Node>(target) ? RestrictionTargetMode.Node : RestrictionTargetMode.Segment;
            m_RestrictionTool.SelectTarget(target);
        }));
        AddBinding(new TriggerBinding<bool>(Mod.Id,"setSegmentBrush", m_RestrictionTool.SetBrushEnabled));
        AddBinding(new TriggerBinding<string, float>(Mod.Id,"setSignAppearance", (key,value) =>
        {
            switch (key)
            {
                case "scale": Mod.Settings.RoadSignScale = SignAppearance.Clamp(value,.5f,2f,1); break;
                case "height": Mod.Settings.RoadSignHeight = SignAppearance.Clamp(value,0,5,0); break;
                case "offset": Mod.Settings.RoadSignLateralOffset = SignAppearance.Clamp(value,-.5f,3,0); break;
                case "spacing": Mod.Settings.RoadPlateSpacing = SignAppearance.Clamp(value,.02f,.2f,.04f); break;
                default: return;
            }
            Mod.Settings.ApplyAndSave(); PublishAppearance();
        }));
        m_Appearance = CreateValue("signAppearance",string.Empty); PublishAppearance();
    }
    private ValueBinding<string> m_Appearance;
    private void PublishAppearance() => m_Appearance.Update(string.Join("|",Format(Mod.Settings.RoadSignScale),Format(Mod.Settings.RoadSignHeight),Format(Mod.Settings.RoadSignLateralOffset),Format(Mod.Settings.RoadPlateSpacing)));
    private void ClearMap() { m_MapSelection.Clear(); m_MapRoadStamps.Clear(); m_MapSnapshot.Update(string.Empty); }
    private void UpdateMapAndBrush()
    {
        m_BrushBinding.Update(m_RestrictionTool.BrushEnabled); m_BrushPendingBinding.Update(m_RestrictionTool.PendingBrushCount);
        // Closed map does not even query the road collection.
        if (!m_MapOpen) return;
        var index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        var visualRevision = World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.Revision ?? 0;
        var geometryChanged = false;
        if (!m_MapChanged.IsEmptyIgnoreFilter)
        {
            using var changed = m_MapChanged.ToEntityArray(Allocator.Temp);
            foreach (var road in changed)
            {
                var stamp = VisualGeometryStamp.Read(EntityManager,road);
                if (!m_MapRoadStamps.TryGetValue(road,out var previous) || previous != stamp) geometryChanged = true;
                m_MapRoadStamps[road] = stamp;
            }
        }
        if (!m_MapDirty && !geometryChanged && m_MapVisualRevision == visualRevision && m_MapIndexRevision == index.Revision) return;
        m_MapDirty = false; m_MapVisualRevision = visualRevision; m_MapIndexRevision = index.Revision;
        var snapshot = new StringBuilder(); m_MapSelection.Clear();
        void Point(float3 point) { snapshot.Append(point.x.ToString("0.##",CultureInfo.InvariantCulture)).Append(',').Append(point.z.ToString("0.##",CultureInfo.InvariantCulture)); }
        using (var roads = m_MapRoads.ToEntityArray(Allocator.Temp))
            foreach (var road in roads)
            {
                var curve = EntityManager.GetComponentData<Curve>(road).m_Bezier;
                m_MapRoadStamps[road] = VisualGeometryStamp.Read(EntityManager,road);
                snapshot.Append("B||");
                for (int i = 0; i <= 8; i++) { if (i > 0) snapshot.Append(';'); Point(Colossal.Mathematics.MathUtils.Position(curve,i/8f)); }
                snapshot.Append('\n');
            }
        using (var targets = m_MapTargets.ToEntityArray(Allocator.Temp))
            foreach (var target in targets)
            {
                if (EntityManager.HasComponent<Deleted>(target)) continue;
                var assets = EntityManager.GetBuffer<RestrictedVehicleAssetV1>(target,true);
                if (assets.Length == 0) continue;
                var key = target.Index + ":" + target.Version; m_MapSelection[key] = target;
                snapshot.Append("R|").Append(key).Append('|');
                if (EntityManager.TryGetComponent(target,out Curve curve))
                    for (int i = 0; i <= 8; i++) { if (i > 0) snapshot.Append(';'); Point(Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,i/8f)); }
                else if (EntityManager.TryGetComponent(target,out Node node)) Point(node.m_Position);
                var entries = index.GetAppliedRoadEntries(target); int active = 0;
                foreach (var entry in entries) if (entry.Enabled) active++;
                snapshot.Append('|').Append(assets.Length).Append('|').Append(active).Append('/').Append(entries.Count).Append('\n');
            }
        m_MapSnapshot.Update(snapshot.ToString());
    }
}
