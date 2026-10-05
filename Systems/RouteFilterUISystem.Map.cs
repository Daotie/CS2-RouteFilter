using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private ValueBinding<int> m_AdvancedClosed;
    private int m_AdvancedCloseRevision;
    private bool m_MapOpen, m_MapDirty = true, m_SecondaryInteraction;
    private int m_AdvancedClosedFrame=-1;
    private ValueBinding<string> m_MapSnapshot, m_MapGeometry;
    private Game.Input.InputBarrier m_ZoomBarrier;
    private string m_AppearanceParameter = "";
    private float m_AppearanceStep = .05f, m_AppearanceSaveAt;
    private bool m_AppearanceSavePending;
    internal Entity AppearancePreviewTarget => m_AppearanceSavePending ? m_RestrictionTool.SelectedTarget : Entity.Null;
    private ValueBinding<bool> m_BrushBinding, m_BrushClearBinding;
    private ValueBinding<int> m_BrushPendingBinding;
    private ValueBinding<string> m_RangeStatus;
    private EntityQuery m_MapRoads, m_MapTargets, m_MapChanged;
    private int m_MapVisualRevision = -1, m_MapIndexRevision = -1;
    private readonly Dictionary<string, Entity> m_MapSelection = new(StringComparer.Ordinal);
    private readonly Dictionary<Entity,uint> m_MapRoadStamps = new();
    private void InitializeMapAndBrush()
    {
        AddBinding(new TriggerBinding<bool>(Mod.Id,"setSecondaryInteraction",open=>m_SecondaryInteraction=open));
        m_AdvancedClosed=CreateValue("advancedClosed",0);
        AddBinding(new TriggerBinding(Mod.Id,"closeAdvancedInteraction",StopAdvancedInteraction));
        AddBinding(new TriggerBinding(Mod.Id,"refreshRestrictionMap",()=>m_MapDirty=true));
        m_MapSnapshot = CreateValue("restrictionMap", string.Empty);
        m_MapGeometry = CreateValue("restrictionMapRoads",string.Empty);
        m_MapTerrain = CreateValue("restrictionMapTerrain",string.Empty);
        m_BrushBinding = CreateValue("segmentBrush", false);
        m_BrushClearBinding = CreateValue("segmentBrushClear",false);
        AddBinding(new TriggerBinding<bool>(Mod.Id,"setSegmentBrushOperation",m_RestrictionTool.SetBrushOperation));
        m_BrushPendingBinding = CreateValue("brushPending", 0);
        m_RangeStatus = CreateValue("rangeStatus","Start");
        m_MapRoads = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
            Any = new[] { ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<TrainTrack>(), ComponentType.ReadOnly<TramTrack>() },
            None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() } });
        m_MapTargets = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_MapChanged = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Edge>() },
            Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
            None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() } });
        AddBinding(new TriggerBinding<bool>(Mod.Id,"setRestrictionMapOpen", open => { m_MapOpen = open; m_MapDirty = true; if (!open) ClearMap(); }));
        AddBinding(new TriggerBinding<string>(Mod.Id,"selectMapTarget", key =>
        {
            if (!m_MapOpen || !m_MapSelection.TryGetValue(key,out var target) || !EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target)) return;
            m_RestrictionTool.SetBrushEnabled(false);
            Mod.SelectedTargetMode = EntityManager.HasComponent<Node>(target) ? RestrictionTargetMode.Node : RestrictionTargetMode.Segment;
            m_RestrictionTool.SelectTarget(target);
        }));
        AddBinding(new TriggerBinding<bool>(Mod.Id,"setSegmentBrush", m_RestrictionTool.SetBrushEnabled));
        AddBinding(new TriggerBinding<bool>(Mod.Id,"confirmSegmentRange",m_RestrictionTool.CommitRange));
        AddBinding(new TriggerBinding(Mod.Id,"cancelSegmentRange",m_RestrictionTool.CancelBrush));
        AddBinding(new TriggerBinding<string,float>(Mod.Id,"setSignAdjustment",(parameter,step) =>
        {
            m_AppearanceParameter = parameter == "wheelStep" || parameter == "height" || parameter == "offset" || parameter == "longitudinal" || parameter == "rotation" ? parameter : "";
            m_AppearanceStep = SignAppearance.Clamp(step,.001f,1f,.05f);
        }));
        AddBinding(new TriggerBinding<string, float>(Mod.Id,"setSignAppearance", (key,value) =>
        {
            switch (key)
            {
                case "height": Mod.Settings.RoadSignHeight = SignAppearance.Clamp(value,0,5,0); break;
                case "offset": Mod.Settings.RoadSignLateralOffset = SignAppearance.Clamp(value,-.5f,3,0); break;
                case "longitudinal": Mod.Settings.RoadSignLongitudinalOffset = SignAppearance.Clamp(value,-10,10,0); break;
                case "rotation": Mod.Settings.RoadSignRotation = SignAppearance.Clamp(value,-180,180,0); break;
                case "spacing": Mod.Settings.RoadPlateSpacing = SignAppearance.Clamp(value,.02f,.2f,.04f); break;
                default: return;
            }
            PublishAppearance(); m_AppearanceSavePending=true; m_AppearanceSaveAt=UnityEngine.Time.realtimeSinceStartup+.4f;
        }));
        AddBinding(new TriggerBinding(Mod.Id,"resetSignAppearance",()=>
        {
            Mod.Settings.RoadSignScale=1; Mod.Settings.RoadSignHeight=0; Mod.Settings.RoadSignLateralOffset=0;
            Mod.Settings.RoadSignLongitudinalOffset=0; Mod.Settings.RoadSignRotation=0; Mod.Settings.RoadPlateSpacing=.04f;
            Mod.Settings.ApplyAndSave(); PublishAppearance(); World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.InvalidateAppearance();
        }));
        m_Appearance = CreateValue("signAppearance",string.Empty); PublishAppearance();
    }
    private ValueBinding<string> m_Appearance;
    private void PublishAppearance() => m_Appearance.Update(string.Join("|",new[]{1f,Mod.Settings.RoadSignHeight,Mod.Settings.RoadSignLateralOffset,Mod.Settings.RoadSignLongitudinalOffset,Mod.Settings.RoadSignRotation}.Select(value=>value.ToString("0.###",CultureInfo.InvariantCulture))));
    private void ClearMap() { m_MapSelection.Clear(); m_MapRoadStamps.Clear(); m_MapSnapshot.Update(string.Empty); m_MapGeometry.Update(string.Empty); }
    private void UpdateMapAndBrush()
    {
        UpdateAppearanceWheel();
        m_BrushBinding.Update(m_RestrictionTool.BrushEnabled);m_BrushClearBinding.Update(m_RestrictionTool.BrushClear); m_BrushPendingBinding.Update(m_RestrictionTool.PendingBrushCount);
        m_RangeStatus.Update(m_RestrictionTool.RangeStatus);
        // Closed map does not even query the road collection.
        if (!m_MapOpen) return;
        UpdateMapTerrain();
        var index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        var visualRevision = World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.Revision ?? 0;
        var geometryChanged = m_MapRoads.CalculateEntityCount() != m_MapRoadStamps.Count;
        geometryChanged |= m_MapGeometryChanged;
        m_MapGeometryChanged = false;
        if (!m_MapDirty && !geometryChanged && m_MapVisualRevision == visualRevision && m_MapIndexRevision == index.Revision) return;
        var rebuildGeometry = m_MapDirty || geometryChanged;
        m_MapDirty = false; m_MapVisualRevision = visualRevision; m_MapIndexRevision = index.Revision;
        if (rebuildGeometry)
        {
            var geometry = new StringBuilder(); m_MapRoadStamps.Clear();
            var minimum = new float2(float.PositiveInfinity); var maximum = new float2(float.NegativeInfinity);
            int extracted=0, rejected=0;
            using var roads = m_MapRoads.ToEntityArray(Allocator.Temp);
            foreach (var road in roads)
            {
                var curve = EntityManager.GetComponentData<Curve>(road).m_Bezier;
                m_MapRoadStamps[road] = VisualGeometryStamp.Read(EntityManager,road);
                if (!math.all(math.isfinite(curve.a)) || !math.all(math.isfinite(curve.b)) || !math.all(math.isfinite(curve.c)) || !math.all(math.isfinite(curve.d))) { rejected++; continue; }
                minimum=math.min(minimum,math.min(math.min(curve.a.xz,curve.b.xz),math.min(curve.c.xz,curve.d.xz)));
                maximum=math.max(maximum,math.max(math.max(curve.a.xz,curve.b.xz),math.max(curve.c.xz,curve.d.xz)));
                extracted++; geometry.Append("B||");
                for (int i=0; i<=8; i++) { if (i>0) geometry.Append(';'); AppendMapPoint(geometry,Colossal.Mathematics.MathUtils.Position(curve,i/8f)); }
                geometry.Append('\n');
            }
            var payload=geometry.ToString(); m_MapGeometry.Update(payload);
            Mod.Log.Info($"[RouteFilter.Map.Geometry] queried={roads.Length} extracted={extracted} rejected={rejected} bytes={Encoding.UTF8.GetByteCount(payload)} bounds={minimum}..{maximum}");
        }
        var snapshot = new StringBuilder(); m_MapSelection.Clear();
        void Point(float3 point) => AppendMapPoint(snapshot,point);
        using (var targets = m_MapTargets.ToEntityArray(Allocator.Temp))
            foreach (var target in targets)
            {
                if (EntityManager.HasComponent<Deleted>(target)) continue;
                var assets = EntityManager.GetBuffer<RestrictedVehicleAssetV1>(target,true);
                if (assets.Length == 0 || (!EntityManager.HasComponent<Road>(target) && !EntityManager.HasComponent<TrainTrack>(target) && !EntityManager.HasComponent<TramTrack>(target) && !EntityManager.HasComponent<Node>(target))) continue;
                var key = target.Index + ":" + target.Version; m_MapSelection[key] = target;
                snapshot.Append("R|").Append(key).Append('|');
                if (EntityManager.TryGetComponent(target,out Curve curve))
                    for (int i = 0; i <= 8; i++) { if (i > 0) snapshot.Append(';'); Point(Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,i/8f)); }
                else if (EntityManager.TryGetComponent(target,out Node node)) Point(node.m_Position);
                var entries = index.GetAppliedRoadEntries(target); int active = 0;
                foreach (var entry in entries) if (entry.Enabled) active++;
                snapshot.Append('|').Append(assets.Length).Append('|').Append(active).Append('/').Append(entries.Count).Append('\n');
                foreach (var entry in entries)
                    if (entry.Enabled && entry.CustomSupported && index.TryGetApproachFrame(entry,true,out var center,out var forward,out var low,out var high))
                    {
                        snapshot.Append("E|").Append(key).Append('|'); Point(center); snapshot.Append(';'); Point(center + math.normalizesafe(forward)*8f); snapshot.Append('\n');
                    }
            }
        var overlayPayload=snapshot.ToString(); m_MapSnapshot.Update(overlayPayload);
        Mod.Log.Info($"[RouteFilter.Map.Overlay] targets={m_MapSelection.Count} bytes={Encoding.UTF8.GetByteCount(overlayPayload)} indexRevision={index.Revision}");
    }
    private bool m_MapGeometryChanged;
    internal void CollectMapGeometryChanges()
    {
        if (!m_MapOpen) return;
        if (!m_MapChanged.IsEmptyIgnoreFilter)
        {
            using var changed = m_MapChanged.ToEntityArray(Allocator.Temp);
            foreach (var road in changed)
            {
                if (EntityManager.HasComponent<Deleted>(road)) { if (m_MapRoadStamps.Remove(road)) m_MapGeometryChanged = true; continue; }
                if(!EntityManager.HasComponent<Road>(road) && !EntityManager.HasComponent<TrainTrack>(road) && !EntityManager.HasComponent<TramTrack>(road))continue;
                var stamp = VisualGeometryStamp.Read(EntityManager,road);
                if (!m_MapRoadStamps.TryGetValue(road,out var previous) || previous != stamp) m_MapGeometryChanged = true;
                m_MapRoadStamps[road] = stamp;
            }
        }
    }
    private static void AppendMapPoint(StringBuilder builder,float3 point) => builder.Append(point.x.ToString("0.##",CultureInfo.InvariantCulture)).Append(',').Append(point.z.ToString("0.##",CultureInfo.InvariantCulture));
    private void UpdateAppearanceWheel()
    {
        var manipulating = m_ToolSystem.activeTool == m_RestrictionTool && (m_AppearanceParameter.Length > 0 || m_MapOpen && m_RestrictionTool.PointerOverUi);
        if (manipulating && m_ZoomBarrier == null) m_ZoomBarrier = Game.Input.InputManager.instance.CreateActionBarrier("Camera","Zoom",Mod.Id+".SignAdjustment");
        if (m_ZoomBarrier != null) m_ZoomBarrier.blocked = manipulating;
        if (m_AppearanceSavePending && UnityEngine.Time.realtimeSinceStartup >= m_AppearanceSaveAt)
        { m_AppearanceSavePending = false; Mod.Settings.ApplyAndSave(); World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.InvalidateAppearance(); }
    }
    internal bool TryCancelAdvancedInteraction()
    {
        if (m_AdvancedClosedFrame==UnityEngine.Time.frameCount) return true;
        if (!m_SecondaryInteraction && !m_MapOpen && m_AppearanceParameter.Length==0 && !m_RestrictionTool.BrushEnabled) return false;
        StopAdvancedInteraction(); return true;
    }
    internal void StopAdvancedInteraction()
    {
        m_SecondaryInteraction=false; m_AdvancedClosedFrame=UnityEngine.Time.frameCount;
        m_AdvancedClosed?.Update(++m_AdvancedCloseRevision);
        m_RestrictionTool.SetPointerOverUi(false);
        m_AppearanceParameter = ""; m_MapOpen = false; m_RestrictionTool.SetBrushEnabled(false); ClearMap();
        m_ZoomBarrier?.Dispose(); m_ZoomBarrier = null;
        if (m_AppearanceSavePending) { m_AppearanceSavePending = false; Mod.Settings.ApplyAndSave(); World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.InvalidateAppearance(); }
    }

}

