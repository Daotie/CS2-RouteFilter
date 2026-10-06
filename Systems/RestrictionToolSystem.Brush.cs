using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RestrictionToolSystem
{
    public bool BrushEnabled { get; private set; }
    private Entity m_RangeStart, m_RangeEnd;
    private ControlPoint m_RangeStartPoint, m_RangeEndPoint;
    private bool m_RangeDragging, m_RangeClear;
    private Entity[] m_RangeAssets = System.Array.Empty<Entity>();
    public bool BrushClear => m_RangeClear;
    public void SetBrushOperation(bool clear) { CancelBrush(); m_RangeClear = clear; }
    private readonly List<Entity> m_Range = new();
    public int PendingBrushCount => m_Range.Count;
    public string RangeStatus => m_RangeDragging ? m_Range.Count == 0 ? "Disconnected" : "Dragging" : "Start";
    private bool m_BatchWrite;
    public void SetBrushEnabled(bool enabled)
    {
        CancelBrush();
        BrushEnabled = enabled && Mod.SelectedTargetMode == RouteFilter.Components.RestrictionTargetMode.Segment;
        UpdateMouseHints();
    }
    public void CancelBrush() { m_RangeDragging = false; m_RangeAssets = System.Array.Empty<Entity>(); m_RangeStart = m_RangeEnd = Entity.Null; m_Range.Clear(); World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.ClearBrushPreview(); }
    private void UpdateMouseHints()
    {
        m_MouseApplyDisplay?.Dispose(); m_MouseCancelDisplay?.Dispose();
        if (m_MouseApplyAction != null) m_MouseApplyDisplay = new Game.Input.DisplayNameOverride(Mod.Id, m_MouseApplyAction,
            BrushEnabled ? "RouteFilter.UI.BrushApply" : "RouteFilter.UI.Select", Game.Input.DisplayNameOverride.kToolTipPriority, Game.Input.InputManager.DeviceType.Mouse);
        if (m_MouseCancelAction != null) m_MouseCancelDisplay = new Game.Input.DisplayNameOverride(Mod.Id, m_MouseCancelAction,
            BrushEnabled ? "RouteFilter.UI.BrushClear" : "RouteFilter.UI.Cancel", Game.Input.DisplayNameOverride.kToolTipPriority, Game.Input.InputManager.DeviceType.Mouse);
        var active = m_ToolSystem.activeTool == this;
        if (m_MouseApplyDisplay != null) m_MouseApplyDisplay.active = active;
        if (m_MouseCancelDisplay != null) m_MouseCancelDisplay.active = active;
    }
    private void UpdateBrushInput()
    {
        if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) CancelBrush();
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) CancelBrush();
    }
    private bool ValidRangeEdge(Entity target) => target != Entity.Null && EntityManager.Exists(target) &&
        EntityManager.HasComponent<Edge>(target) && GetTransportMode(target) != 0 &&
        !EntityManager.HasComponent<Deleted>(target) && !EntityManager.HasComponent<Game.Tools.Temp>(target);
    private ControlPoint RangePoint(Entity target, float3 hit)
    {
        var point = new ControlPoint { m_OriginalEntity = target, m_Position = hit, m_HitPosition = hit };
        if (EntityManager.TryGetComponent(target,out Curve curve))
        {
            float best=float.MaxValue;
            for(int i=0;i<=32;i++)
            {
                float t=i/32f;var position=Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,t);
                float distance=math.distancesq(position,hit);
                if(distance<best){best=distance;point.m_CurvePosition=t;point.m_Position=position;}
            }
        }
        return point;
    }
    private List<Entity> NativeRange(ControlPoint start, ControlPoint end)
    {
        var result=new List<Entity>();
        if(!ValidRangeEdge(start.m_OriginalEntity)||!ValidRangeEdge(end.m_OriginalEntity)) return result;
        if(!EntityManager.TryGetComponent(start.m_OriginalEntity,out PrefabRef reference) ||
            !EntityManager.TryGetComponent(reference.m_Prefab,out NetData net) ||
            !EntityManager.TryGetComponent(reference.m_Prefab,out PlaceableNetData placeable)) return result;
        // Public vanilla helper reads topology/prefab compatibility and writes only
        // this disposable path. Never activate NetToolSystem or invoke Apply/Replace.
        CompleteDependency();
        var edges=GetComponentLookup<Edge>(true);var nodes=GetComponentLookup<Game.Net.Node>(true);
        var curves=GetComponentLookup<Curve>(true);var prefabs=GetComponentLookup<PrefabRef>(true);
        var nets=GetComponentLookup<NetData>(true);var connected=GetBufferLookup<ConnectedEdge>(true);
        using var path=new NativeList<NetToolSystem.PathEdge>(Allocator.Temp);
        try
        {
            NetToolSystem.CreatePath(start,end,path,net,placeable,ref edges,ref nodes,ref curves,ref prefabs,ref nets,ref connected);
            if(path.Length>4096)return result;
            for(int i=0;i<path.Length;i++)
            {
                var entity=path[i].m_Entity;
                if(!ValidRangeEdge(entity))return new List<Entity>();
                if(!result.Contains(entity))result.Add(entity);
            }
            if(!result.Contains(start.m_OriginalEntity)||!result.Contains(end.m_OriginalEntity))result.Clear();
        }
        catch(System.Exception error){Mod.Log.Warn("[RouteFilter.Batch] native selection unavailable: "+error.Message);result.Clear();}
        return result;
    }
    private RouteFilter.Components.BrushButtonInput BrushButtons => new(
        Mod.Apply?.WasPressedThisFrame() == true, Mod.Clear?.WasPressedThisFrame() == true,
        Mod.Apply?.WasReleasedThisFrame() == true, Mod.Clear?.WasReleasedThisFrame() == true);
    private bool BrushButtonReleased => m_RangeDragging && BrushButtons.Released(m_RangeClear);
    private void CollectBrushTarget(Entity target, float3 hit)
    {
        if(PointerOverUi || UnityEngine.Time.frameCount<=m_ActivationFrame)return;
        var buttons=BrushButtons;
        bool pressed=buttons.Pressed;
        if(pressed)
        {
            CancelBrush();
            if(!ValidRangeEdge(target))return;
            m_RangeClear=buttons.ClearPressed;m_RangeDragging=true;m_RangeStart=target;m_RangeAssets=Mod.SelectedVehicleAssets.ToArray();
            m_RangeStartPoint=RangePoint(target,hit);
        }
        if(!m_RangeDragging)return;
        bool released=buttons.Released(m_RangeClear);
        if(!ValidRangeEdge(target)){m_Range.Clear();World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.ClearBrushPreview();if(released)CancelBrush();return;}
        var candidate=RangePoint(target,hit);
        if(target!=m_RangeEnd || pressed || math.abs(candidate.m_CurvePosition-m_RangeEndPoint.m_CurvePosition)>.03f)
        {
            m_RangeEnd=target;m_RangeEndPoint=candidate;
            m_Range.Clear();m_Range.AddRange(NativeRange(m_RangeStartPoint,m_RangeEndPoint));
            World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.PreviewRange(m_Range);
        }
        if(released)CommitRange(m_RangeClear);
    }
    public void CommitRange(bool clear)
    {
        if (!BrushEnabled || m_RangeEnd == Entity.Null || m_Range.Count == 0 ||
            !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) { CancelBrush(); return; }
        var current = NativeRange(m_RangeStartPoint,m_RangeEndPoint);
        if (!current.SequenceEqual(m_Range) || m_Range.Any(edge => !ValidRangeEdge(edge) || GetTransportMode(edge) == 0)) { CancelBrush(); return; }
        var assets = m_RangeAssets;
        if(!clear && assets.Any(asset=>!EntityManager.Exists(asset)||(!EntityManager.HasComponent<CarData>(asset)&&!EntityManager.HasComponent<TrainData>(asset)))){CancelBrush();return;}
        var persistence=World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>();
        var directions=m_Range.ToDictionary(target=>target,target=>persistence.GetDirectionIntent(target));
        var previous = new Dictionary<Entity, Entity[]>();
        foreach (var target in m_Range)
            previous[target] = EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RouteFilter.Components.RestrictedVehicleAssetV1> saved)
                ? RouteFilter.Persistence.AssetLibrarySnapshot.Read(saved.Length, i => saved[i].m_Prefab) : null;
        bool committed = false;
        World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>().ReleaseAll();
        World.GetOrCreateSystemManaged<RailEnforcementBackend>().ReleaseAll();
        m_BatchWrite = true;
        try
        {
            foreach (var target in m_Range) if (clear) ClearRestriction(target); else SetRestriction(target,assets,directions[target]);
            Mod.Log.Info($"[RouteFilter.Batch] release committed targets={m_Range.Count} clear={clear} nativePath=true perTargetDirections=true");
            committed = true;
        }
        catch (System.Exception error)
        {
            foreach(var target in m_Range)
                if(previous[target] == null) ClearRestriction(target);
                else RestoreRestriction(target,false,previous[target],directions[target]);
            Mod.Log.Warn("[RouteFilter.Batch] transaction rolled back: " + error.Message);
        }
        finally { m_BatchWrite = false; CancelBrush(); }
        if(committed && !clear) World.GetExistingSystemManaged<RouteFilterUISystem>()?.RecordRecentAssets(assets);
    }
}
