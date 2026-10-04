using Colossal.Entities;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using RouteFilter.Components;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetSubLane = Game.Net.SubLane;

namespace RouteFilter.Systems;

public sealed partial class RestrictionToolSystem : ToolBaseSystem
{
    private DisplayNameOverride m_MouseApplyDisplay = null!;
    private DisplayNameOverride m_MouseCancelDisplay = null!;
    private int m_ActivationFrame;
    public Entity HoveredTarget { get; private set; } = Entity.Null;
    public int HoveredTransportMode { get; private set; }
    public Entity SelectedTarget { get; private set; } = Entity.Null;
    public int SelectedTransportMode { get; private set; }
    public bool PointerOverUi { get; private set; }

    public override string toolID => "RouteFilterTool";
    public override bool allowUnderground => true;

    protected override void OnCreate()
    {
        base.OnCreate();
        // ToolBaseSystem obtains these actions from its internal tool collection.
        // Resolve the same collection so the game's InputHintsTooltipSystem can
        // place the hint beside the cursor instead of drawing a web overlay.
        var inputType = typeof(InputManager);
        var collection = inputType.GetProperty("toolActionCollection", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(InputManager.instance)
            ?? inputType.GetField("toolActionCollection", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(InputManager.instance);
        var getAction = collection?.GetType().GetMethod("GetActionState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var source = GetType().Name;
        var mouseApply = getAction?.Invoke(collection, new object[] { "Mouse Apply", source }) as ProxyAction;
        var mouseCancel = getAction?.Invoke(collection, new object[] { "Mouse Cancel", source }) as ProxyAction;
        if (mouseApply != null)
        {
            m_MouseApplyDisplay = new DisplayNameOverride(Mod.Id, mouseApply, "RouteFilter.UI.Select", DisplayNameOverride.kToolTipPriority, InputManager.DeviceType.Mouse);
            m_MouseApplyDisplay.active = false;
        }
        if (mouseCancel != null)
        {
            m_MouseCancelDisplay = new DisplayNameOverride(Mod.Id, mouseCancel, "RouteFilter.UI.Cancel", DisplayNameOverride.kToolTipPriority, InputManager.DeviceType.Mouse);
            m_MouseCancelDisplay.active = false;
        }
        Mod.Log.Info($"[RouteFilter.Tool] native hints apply={(mouseApply != null)} cancel={(mouseCancel != null)} source={source}");
    }

    protected override void OnDestroy()
    {
        m_MouseApplyDisplay?.Dispose();
        m_MouseCancelDisplay?.Dispose();
        base.OnDestroy();
    }

    public override PrefabBase GetPrefab() => null;
    public override bool TrySetPrefab(PrefabBase prefab) => false;

    public void Toggle()
    {
        if (m_ToolSystem.activeTool == this)
            Deactivate();
        else
            Activate();
    }

    public void Activate()
    {
        Mod.Log.Info($"[RouteFilter.Tool] Activated {Mod.SelectedTargetMode}");
        PointerOverUi = false;
        m_ActivationFrame = UnityEngine.Time.frameCount;
        if (m_ToolSystem.activeTool == this) return;
        P0Diagnostics.Arm(World, m_ToolSystem.selected);
        m_ToolSystem.selected = Entity.Null;
        m_ToolSystem.activeTool = this;
    }

    public void Deactivate()
    {
        if (m_ToolSystem.activeTool != this) return;
        HoveredTarget = Entity.Null;
        HoveredTransportMode = 0;
        ClearSelection();
        m_ToolSystem.selected = Entity.Null;
        m_ToolSystem.activeTool = m_DefaultToolSystem;
    }

    protected override void OnStopRunning()
    {
        if (m_MouseApplyDisplay != null) m_MouseApplyDisplay.active = false;
        if (m_MouseCancelDisplay != null) m_MouseCancelDisplay.active = false;
        HoveredTarget = Entity.Null;
        HoveredTransportMode = 0;
        PointerOverUi = false;
        SelectedTarget = Entity.Null;
        SelectedTransportMode = 0;
        ClearEntryEditor();
        World.GetExistingSystemManaged<RouteFilterUISystem>()?.NotifyPanelClose();
        base.OnStopRunning();
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        if (m_MouseApplyDisplay != null) m_MouseApplyDisplay.active = true;
        if (m_MouseCancelDisplay != null) m_MouseCancelDisplay.active = true;
    }

    public override void InitializeRaycast()
    {
        base.InitializeRaycast();
        if (!P0Diagnostics.Tool) { m_ToolRaycastSystem.typeMask = (TypeMask)0; return; }
        m_ToolRaycastSystem.typeMask = TypeMask.Net;
        m_ToolRaycastSystem.netLayerMask = Layer.Road | Layer.PublicTransportRoad |
                                               Layer.TrainTrack | Layer.TramTrack | Layer.SubwayTrack;
        m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground | CollisionMask.Underground;
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        // The base tool gate is bypassed by this override, so guard explicitly: outside of an
        // active session every frame would otherwise schedule a net raycast and write
        // m_ToolSystem.selected, fighting the active vanilla tool.
        if (m_ToolSystem.activeTool != this) return inputDeps;
        if (!P0Diagnostics.Tool) { m_ToolSystem.selected = Entity.Null; return inputDeps; }

        // Handle native cancel independently of raycast/pointer state. First cancel clears
        // the selected target; another cancel with no target closes the panel.
        if ((Mod.Clear != null && Mod.Clear.WasPressedThisFrame()) || cancelAction.WasPressedThisFrame())
        {
            if (SelectedTarget != Entity.Null) ClearSelection();
            else
            {
                Deactivate();
                World.GetOrCreateSystemManaged<RouteFilterUISystem>().NotifyPanelClose();
            }
            return inputDeps;
        }

        if (SelectedTarget != Entity.Null && !EntityManager.Exists(SelectedTarget)) ClearSelection();
        RefreshEntryEditor();

        if (PointerOverUi)
        {
            HoveredTarget = Entity.Null;
            HoveredTransportMode = 0;
            m_ToolSystem.selected = P0Diagnostics.Highlight ? SelectedTarget : Entity.Null;
            return inputDeps;
        }

        if (!GetRaycastResult(out Entity entity, out RaycastHit hit))
        {
            HoveredTarget = Entity.Null;
            HoveredTransportMode = 0;
            m_ToolSystem.selected = P0Diagnostics.Highlight ? SelectedTarget : Entity.Null;
            return inputDeps;
        }

        if (UnityEngine.Time.frameCount > m_ActivationFrame && Mod.Apply != null &&
            Mod.Apply.WasPressedThisFrame() && TryToggleEntry(hit.m_HitPosition)) return inputDeps;

        var target = ResolveTarget(entity, hit.m_HitPosition);
        if (HoveredTarget != target) Mod.Log.Debug($"[RouteFilter.Tool] Hover {Mod.SelectedTargetMode}={target}");
        HoveredTarget = target;
        HoveredTransportMode = GetTransportMode(target);
        m_ToolSystem.selected = P0Diagnostics.Highlight ? (SelectedTarget != Entity.Null ? SelectedTarget : target) : Entity.Null;
        if (target != Entity.Null && UnityEngine.Time.frameCount > m_ActivationFrame &&
            Mod.Apply != null && Mod.Apply.WasPressedThisFrame()) SelectTarget(target);

        return inputDeps;
    }

    private Entity ResolveTarget(Entity entity, float3 hitPosition)
    {
        var edgeEntity = FindOwningEdge(entity);
        if (Mod.SelectedTargetMode == RestrictionTargetMode.Segment) return edgeEntity;

        if (EntityManager.HasComponent<Node>(entity))
            return entity;

        if (edgeEntity == Entity.Null || !EntityManager.TryGetComponent(edgeEntity, out Edge edge))
            return Entity.Null;

        if (!EntityManager.TryGetComponent(edge.m_Start, out Node start) ||
            !EntityManager.TryGetComponent(edge.m_End, out Node end))
            return Entity.Null;

        return math.distancesq(start.m_Position, hitPosition) <= math.distancesq(end.m_Position, hitPosition)
            ? edge.m_Start
            : edge.m_End;
    }

    private Entity FindOwningEdge(Entity entity)
    {
        var current = entity;
        for (var depth = 0; depth < 8 && current != Entity.Null; depth++)
        {
            if (EntityManager.HasComponent<Edge>(current)) return current;
            if (!EntityManager.TryGetComponent(current, out Owner owner) || owner.m_Owner == current) break;
            current = owner.m_Owner;
        }
        return Entity.Null;
    }

    public void SetPointerOverUi(bool value) => PointerOverUi = value;

    public void SelectTarget(Entity target)
    {
        if (target == Entity.Null || target == SelectedTarget) return;
        SelectedTarget = target;
        SelectedTransportMode = GetTransportMode(target);
        LoadEntryEditor();
        m_ToolSystem.selected = target;
        Mod.Log.Info($"[RouteFilter.Tool] Selected {(EntityManager.HasComponent<Node>(target) ? "Node" : "Segment")}={target.Index}:{target.Version}");
    }

    public void ClearSelection()
    {
        if (SelectedTarget != Entity.Null) Mod.Log.Info("[RouteFilter.Tool] Selection cancelled");
        SelectedTarget = Entity.Null;
        SelectedTransportMode = 0;
        ClearEntryEditor();
        m_ToolSystem.selected = Entity.Null;
    }

    public void ApplySelection()
    {
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return;
        if (SelectedTarget == Entity.Null)
        {
            Mod.Log.Warn("Apply ignored: no node or segment selected");
            return;
        }
        RefreshEntryEditor();
        SetRestriction(SelectedTarget, Mod.SelectedVehicleAssets, PendingEntries());
        Mod.Log.Info($"[RouteFilter.Tool] restriction applied target={SelectedTarget}");
    }

    public void ClearSelectedRestriction()
    {
        if (SelectedTarget == Entity.Null) return;
        ClearRestriction(SelectedTarget);
        SetAllEntryDirections();
        Mod.Log.Info($"[RouteFilter.Tool] restriction cleared target={SelectedTarget.Index}:{SelectedTarget.Version}");
    }

    public void SetRestriction(Entity target, IReadOnlyCollection<Entity> vehicleAssets,
        RouteFilter.Persistence.RestrictionEntryIdentity[] entries = null)
    {
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return;
        World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>().ReleaseAll();
        World.GetOrCreateSystemManaged<RailEnforcementBackend>().ReleaseAll();
        var isNode = EntityManager.HasComponent<Node>(target);
        var isSegment = EntityManager.HasComponent<Edge>(target);
        if (!isNode && !isSegment) return;
        Mod.RestrictionsDirty = true;

        var transportMode = GetTransportMode(target);
        var compatibleAssets = new List<Entity>();
        foreach (var asset in vehicleAssets)
        {
            if ((transportMode & 1) != 0 && EntityManager.HasComponent<CarData>(asset)) compatibleAssets.Add(asset);
            else if ((transportMode & 2) != 0 && EntityManager.HasComponent<TrainData>(asset)) compatibleAssets.Add(asset);
        }
        if (compatibleAssets.Count == 0)
        {
            ClearRestriction(target);
            Mod.Log.Info($"{(isNode ? "Node" : "Segment")} {target.Index}:{target.Version} set to allow all compatible vehicle assets");
            return;
        }

        World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ForgetPending(target);
        if (isNode) SetNodeRestriction(target, compatibleAssets);
        else SetSegmentRestriction(target, compatibleAssets);
        World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().RememberIntent(target, isNode ? (byte)0 : (byte)1, entries);
        World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.MarkDirty(target);

        Mod.Log.Info($"{(isNode ? "Node" : "Segment")} {target.Index}:{target.Version} forbidden list set to {compatibleAssets.Count} compatible vehicle assets");
    }

    public void ClearRestriction(Entity target)
    {
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return;
        World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ForgetPending(target);
        World.GetOrCreateSystemManaged<RoadEnforcementCoordinator>().ReleaseAll();
        World.GetOrCreateSystemManaged<RailEnforcementBackend>().ReleaseAll();
        Mod.RestrictionsDirty = true;
        if (EntityManager.HasComponent<NodeAssetRestrictionV1>(target)) EntityManager.RemoveComponent<NodeAssetRestrictionV1>(target);
        if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(target)) EntityManager.RemoveComponent<SegmentAssetRestrictionV1>(target);
        if (EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target)) EntityManager.RemoveComponent<RestrictedVehicleAssetV1>(target);
        World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.MarkDirty(target);
    }

    /// <summary>
    /// Restores a restriction from the save payload. Writes the marker and asset buffer
    /// directly; unlike <see cref="SetRestriction"/> it never clears the target when the
    /// list is empty, so an unresolved restore cannot destroy already-restored data.
    /// </summary>
    public void RestoreRestriction(Entity target, bool isNode, IReadOnlyCollection<Entity> vehicleAssets,
        RouteFilter.Persistence.RestrictionEntryIdentity[] entries = null)
    {
        if (!World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return;
        if (isNode) SetNodeRestriction(target, vehicleAssets);
        else SetSegmentRestriction(target, vehicleAssets);
        World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().SetDirectionIntent(target, entries);
        World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.MarkDirty(target);
        Mod.RestrictionsDirty = true;
    }

    private void SetNodeRestriction(Entity target, IReadOnlyCollection<Entity> vehicleAssets)
    {
        if (!EntityManager.HasComponent<NodeAssetRestrictionV1>(target))
            EntityManager.AddComponentData(target, new NodeAssetRestrictionV1 { m_Schema = 1 });
        WriteAssetBuffer(target, vehicleAssets);
    }

    private void SetSegmentRestriction(Entity target, IReadOnlyCollection<Entity> vehicleAssets)
    {
        if (!EntityManager.HasComponent<SegmentAssetRestrictionV1>(target))
            EntityManager.AddComponentData(target, new SegmentAssetRestrictionV1 { m_Schema = 1 });
        WriteAssetBuffer(target, vehicleAssets);
    }

    private void WriteAssetBuffer(Entity target, IReadOnlyCollection<Entity> vehicleAssets)
    {
        var buffer = EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target)
            ? EntityManager.GetBuffer<RestrictedVehicleAssetV1>(target)
            : EntityManager.AddBuffer<RestrictedVehicleAssetV1>(target);
        buffer.Clear();
        foreach (var prefab in vehicleAssets)
            if (prefab != Entity.Null) buffer.Add(new RestrictedVehicleAssetV1(prefab));
    }

    public int GetTransportMode(Entity target)
    {
        if (target == Entity.Null) return 0;
        var mode = GetLaneMode(target);
        if (mode != 0 || !EntityManager.TryGetBuffer(target, true, out DynamicBuffer<ConnectedEdge> edges)) return mode;
        foreach (var edge in edges) mode |= GetLaneMode(edge.m_Edge);
        return mode;
    }

    private int GetLaneMode(Entity target)
    {
        var mode = 0;
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<NetSubLane> lanes)) return mode;
        foreach (var subLane in lanes)
        {
            if (EntityManager.HasComponent<Game.Net.CarLane>(subLane.m_SubLane)) mode |= 1;
            if (EntityManager.HasComponent<Game.Net.TrackLane>(subLane.m_SubLane)) mode |= 2;
        }
        return mode;
    }
}
