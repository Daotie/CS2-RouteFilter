using System.Collections.Generic;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace RouteFilter.Systems;

public sealed partial class RestrictionToolSystem
{
    public bool BrushEnabled { get; private set; }
    private readonly RouteFilter.Persistence.PendingBrushBatch<Entity> m_Brush = new();
    public int PendingBrushCount => m_Brush.Targets.Count;
    private bool m_BatchWrite;
    public void SetBrushEnabled(bool enabled)
    {
        CancelBrush();
        BrushEnabled = enabled && Mod.SelectedTargetMode == RouteFilter.Components.RestrictionTargetMode.Segment;
        UpdateMouseHints();
    }
    public void CancelBrush() { m_Brush.Reset(); World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.ClearBrushPreview(); }
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
        if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) { CancelBrush(); return; }
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) { CancelBrush(); return; }
        if (!m_Brush.Active && !PointerOverUi && UnityEngine.Time.frameCount > m_ActivationFrame)
        {
            var apply = Mod.Apply != null && Mod.Apply.WasPressedThisFrame();
            var clear = Mod.Clear != null && Mod.Clear.WasPressedThisFrame();
            if (apply || clear)
            {
                m_Brush.Begin(clear,Mod.SelectedVehicleAssets);
            }
        }
        if (m_Brush.Active && !(m_Brush.Clear ? Mod.Clear.IsPressed() : Mod.Apply.IsPressed())) CommitBrush();
    }
    private void CollectBrushTarget(Entity target)
    {
        if (!PointerOverUi && target != Entity.Null &&
            EntityManager.HasComponent<Edge>(target) && !EntityManager.HasComponent<Deleted>(target) && m_Brush.Add(target))
            World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.PreviewBrushTarget(target);
    }
    private void CommitBrush()
    {
        if (m_Brush.Targets.Count == 0) { CancelBrush(); return; }
        World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>().ReleaseAll();
        World.GetOrCreateSystemManaged<RailEnforcementBackend>().ReleaseAll();
        m_BatchWrite = true;
        try
        {
            foreach (var target in m_Brush.Targets)
            {
                if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target)) continue;
                if (m_Brush.Clear) ClearRestriction(target); else SetRestriction(target, m_Brush.Assets);
            }
            if (!m_Brush.Clear) World.GetExistingSystemManaged<RouteFilterUISystem>()?.RecordRecentAssets(m_Brush.Assets);
        }
        finally { m_BatchWrite = false; CancelBrush(); }
    }
}
