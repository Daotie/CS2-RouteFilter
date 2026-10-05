using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RestrictionToolSystem
{
    public bool BrushEnabled { get; private set; }
    private Entity m_RangeStart, m_RangeEnd;
    private readonly List<Entity> m_Range = new();
    public int PendingBrushCount => m_Range.Count;
    public string RangeStatus => m_RangeStart == Entity.Null ? "Start" : m_RangeEnd == Entity.Null ? "End" : m_Range.Count == 0 ? "Disconnected" : "Ready";
    private bool m_BatchWrite;
    public void SetBrushEnabled(bool enabled)
    {
        CancelBrush();
        BrushEnabled = enabled && Mod.SelectedTargetMode == RouteFilter.Components.RestrictionTargetMode.Segment;
        UpdateMouseHints();
    }
    public void CancelBrush() { m_RangeStart = m_RangeEnd = Entity.Null; m_Range.Clear(); World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.ClearBrushPreview(); }
    private void UpdateMouseHints()
    {
        m_MouseApplyDisplay?.Dispose(); m_MouseCancelDisplay?.Dispose();
        if (m_MouseApplyAction != null) m_MouseApplyDisplay = new Game.Input.DisplayNameOverride(Mod.Id, m_MouseApplyAction,
            BrushEnabled ? "RouteFilter.UI.BrushApply" : "RouteFilter.UI.Select", Game.Input.DisplayNameOverride.kToolTipPriority, Game.Input.InputManager.DeviceType.Mouse);
        if (m_MouseCancelAction != null) m_MouseCancelDisplay = new Game.Input.DisplayNameOverride(Mod.Id, m_MouseCancelAction,
            BrushEnabled ? "RouteFilter.UI.Cancel" : "RouteFilter.UI.Cancel", Game.Input.DisplayNameOverride.kToolTipPriority, Game.Input.InputManager.DeviceType.Mouse);
        var active = m_ToolSystem.activeTool == this;
        if (m_MouseApplyDisplay != null) m_MouseApplyDisplay.active = active;
        if (m_MouseCancelDisplay != null) m_MouseCancelDisplay.active = active;
    }
    private void UpdateBrushInput()
    {
        if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
            (!PointerOverUi && Mod.Clear?.WasPressedThisFrame() == true)) CancelBrush();
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) CancelBrush();
    }
    private bool ValidRangeEdge(Entity target) => target != Entity.Null && EntityManager.Exists(target) &&
        EntityManager.HasComponent<Edge>(target) && EntityManager.HasComponent<Road>(target) &&
        !EntityManager.HasComponent<Deleted>(target) && !EntityManager.HasComponent<Game.Tools.Temp>(target);
    private IEnumerable<Entity> RangeNeighbours(Entity edge)
    {
        if (!ValidRangeEdge(edge)) return System.Array.Empty<Entity>();
        var data = EntityManager.GetComponentData<Edge>(edge);
        var result = new HashSet<Entity>();
        foreach (var node in new[] { data.m_Start,data.m_End })
            if (EntityManager.TryGetBuffer(node,true,out DynamicBuffer<ConnectedEdge> connected))
                foreach (var item in connected) if (item.m_Edge != edge && ValidRangeEdge(item.m_Edge)) result.Add(item.m_Edge);
        return result.OrderBy(item => item.Index).ThenBy(item => item.Version);
    }
    private void CollectBrushTarget(Entity target)
    {
        if (PointerOverUi || UnityEngine.Time.frameCount <= m_ActivationFrame || Mod.Apply?.WasPressedThisFrame() != true || !ValidRangeEdge(target)) return;
        if (m_RangeStart == Entity.Null || m_RangeEnd != Entity.Null) { CancelBrush(); m_RangeStart = target; }
        else m_RangeEnd = target;
        m_Range.Clear();
        if (m_RangeEnd == Entity.Null) m_Range.Add(m_RangeStart);
        else m_Range.AddRange(RouteFilter.Persistence.ConnectedRoadRange.Find(m_RangeStart,m_RangeEnd,RangeNeighbours,16384));
        var preview = World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>();
        preview?.PreviewRange(m_Range);
    }
    public void CommitRange(bool clear)
    {
        if (!BrushEnabled || m_RangeEnd == Entity.Null || m_Range.Count == 0 ||
            !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return;
        var current = RouteFilter.Persistence.ConnectedRoadRange.Find(m_RangeStart,m_RangeEnd,RangeNeighbours,16384);
        if (!current.SequenceEqual(m_Range) || m_Range.Any(edge => !ValidRangeEdge(edge))) { CancelBrush(); return; }
        var assets = Mod.SelectedVehicleAssets.ToArray();
        World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>().ReleaseAll();
        World.GetOrCreateSystemManaged<RailEnforcementBackend>().ReleaseAll();
        m_BatchWrite = true;
        try
        {
            foreach (var target in m_Range) if (clear) ClearRestriction(target); else SetRestriction(target,assets);
            if (!clear) World.GetExistingSystemManaged<RouteFilterUISystem>()?.RecordRecentAssets(assets);
        }
        finally { m_BatchWrite = false; CancelBrush(); }
    }
}
