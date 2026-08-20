using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using RouteFilter.Components;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetSubLane = Game.Net.SubLane;

namespace RouteFilter.Systems;

public sealed partial class RestrictionToolSystem : ToolBaseSystem
{
    public Entity HoveredTarget { get; private set; } = Entity.Null;
    public int HoveredTransportMode { get; private set; }
    public Entity SelectedTarget { get; private set; } = Entity.Null;
    public int SelectedTransportMode { get; private set; }
    public bool PointerOverUi { get; private set; }

    public override string toolID => "RouteFilterTool";
    public override bool allowUnderground => true;

    public override PrefabBase GetPrefab() => null;
    public override bool TrySetPrefab(PrefabBase prefab) => false;

    public void Toggle()
    {
        if (m_ToolSystem.activeTool == this)
        {
            HoveredTarget = Entity.Null;
            HoveredTransportMode = 0;
            ClearSelection();
            m_ToolSystem.selected = Entity.Null;
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }
        else
        {
            m_ToolSystem.selected = Entity.Null;
            m_ToolSystem.activeTool = this;
        }
    }

    public override void InitializeRaycast()
    {
        base.InitializeRaycast();
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

        if (SelectedTarget != Entity.Null && !EntityManager.Exists(SelectedTarget)) ClearSelection();

        if (PointerOverUi)
        {
            HoveredTarget = Entity.Null;
            HoveredTransportMode = 0;
            m_ToolSystem.selected = SelectedTarget;
            return inputDeps;
        }

        if (!GetRaycastResult(out Entity entity, out RaycastHit hit))
        {
            HoveredTarget = Entity.Null;
            HoveredTransportMode = 0;
            m_ToolSystem.selected = SelectedTarget;
            if (Mod.Clear.WasPressedThisFrame()) ClearSelection();
            return inputDeps;
        }

        var target = ResolveTarget(entity, hit.m_HitPosition);
        HoveredTarget = target;
        HoveredTransportMode = GetTransportMode(target);
        m_ToolSystem.selected = SelectedTarget != Entity.Null ? SelectedTarget : target;
        if (Mod.Clear.WasPressedThisFrame())
        {
            ClearSelection();
            return inputDeps;
        }
        if (target != Entity.Null && Mod.Apply.WasPressedThisFrame()) SelectTarget(target);

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
        if (target == Entity.Null) return;
        SelectedTarget = target;
        SelectedTransportMode = GetTransportMode(target);
        m_ToolSystem.selected = target;
        Mod.Log.Info($"Selected {(EntityManager.HasComponent<Node>(target) ? "node" : "segment")} {target.Index}:{target.Version}");
    }

    public void ClearSelection()
    {
        SelectedTarget = Entity.Null;
        SelectedTransportMode = 0;
        m_ToolSystem.selected = Entity.Null;
    }

    public void ApplySelection()
    {
        if (SelectedTarget == Entity.Null)
        {
            Mod.Log.Warn("Apply ignored: no node or segment selected");
            return;
        }
        SetRestriction(SelectedTarget, Mod.SelectedVehicleAssets);
    }

    public void ClearSelectedRestriction()
    {
        if (SelectedTarget == Entity.Null) return;
        ClearRestriction(SelectedTarget);
        Mod.Log.Info($"Restrictions cleared from {SelectedTarget.Index}:{SelectedTarget.Version}");
    }

    public void SetRestriction(Entity target, IReadOnlyCollection<Entity> vehicleAssets)
    {
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

        if (isNode) SetNodeRestriction(target, compatibleAssets);
        else SetSegmentRestriction(target, compatibleAssets);

        MarkTargetLanesUpdated(target);
        Mod.Log.Info($"{(isNode ? "Node" : "Segment")} {target.Index}:{target.Version} forbidden list set to {compatibleAssets.Count} compatible vehicle assets");
    }

    public void ClearRestriction(Entity target)
    {
        Mod.RestrictionsDirty = true;
        if (EntityManager.HasComponent<NodeAssetRestrictionV1>(target)) EntityManager.RemoveComponent<NodeAssetRestrictionV1>(target);
        if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(target)) EntityManager.RemoveComponent<SegmentAssetRestrictionV1>(target);
        if (EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target)) EntityManager.RemoveComponent<RestrictedVehicleAssetV1>(target);
        MarkTargetLanesUpdated(target);
    }

    /// <summary>
    /// Restores a restriction from the save payload. Writes the marker and asset buffer
    /// directly; unlike <see cref="SetRestriction"/> it never clears the target when the
    /// list is empty, so an unresolved restore cannot destroy already-restored data.
    /// </summary>
    public void RestoreRestriction(Entity target, bool isNode, IReadOnlyCollection<Entity> vehicleAssets)
    {
        if (isNode) SetNodeRestriction(target, vehicleAssets);
        else SetSegmentRestriction(target, vehicleAssets);
        MarkTargetLanesUpdated(target);
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

    private void MarkTargetLanesUpdated(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<NetSubLane> lanes)) return;
        foreach (var subLane in lanes)
            if (!EntityManager.HasComponent<Updated>(subLane.m_SubLane))
                EntityManager.AddComponent<Updated>(subLane.m_SubLane);
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
