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
    public override string toolID => "RouteFilterTool";
    public override bool allowUnderground => true;

    public override PrefabBase GetPrefab() => null;
    public override bool TrySetPrefab(PrefabBase prefab) => false;

    public void Toggle()
    {
        if (m_ToolSystem.activeTool == this)
        {
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
        if (!GetRaycastResult(out Entity entity, out RaycastHit hit))
        {
            m_ToolSystem.selected = Entity.Null;
            return inputDeps;
        }

        var target = ResolveTarget(entity, hit.m_HitPosition);
        m_ToolSystem.selected = target;
        if (target == Entity.Null)
            return inputDeps;

        if (Mod.Apply.WasPressedThisFrame())
            SetRestriction(target, Mod.SelectedVehicleAssets);
        else if (Mod.Clear.WasPressedThisFrame())
            ClearRestriction(target);

        return inputDeps;
    }

    private Entity ResolveTarget(Entity entity, float3 hitPosition)
    {
        if (Mod.SelectedTargetMode == RestrictionTargetMode.Segment)
            return EntityManager.HasComponent<Edge>(entity) ? entity : Entity.Null;

        if (EntityManager.HasComponent<Node>(entity))
            return entity;

        if (!EntityManager.TryGetComponent(entity, out Edge edge))
            return Entity.Null;

        if (!EntityManager.TryGetComponent(edge.m_Start, out Node start) ||
            !EntityManager.TryGetComponent(edge.m_End, out Node end))
            return Entity.Null;

        return math.distancesq(start.m_Position, hitPosition) <= math.distancesq(end.m_Position, hitPosition)
            ? edge.m_Start
            : edge.m_End;
    }

    public void SetRestriction(Entity target, IReadOnlyCollection<Entity> vehicleAssets)
    {
        var isNode = EntityManager.HasComponent<Node>(target);
        var isSegment = EntityManager.HasComponent<Edge>(target);
        if (!isNode && !isSegment) return;

        if (vehicleAssets.Count == 0) ClearRestriction(target);
        else if (isNode) SetNodeRestriction(target, vehicleAssets);
        else SetSegmentRestriction(target, vehicleAssets);

        MarkTargetLanesUpdated(target);
        Mod.Log.Info($"{(isNode ? "Node" : "Segment")} {target.Index}:{target.Version} restrictions set to {vehicleAssets.Count} vehicle assets");
    }

    public void ClearRestriction(Entity target)
    {
        if (EntityManager.HasComponent<NodeAssetRestrictionV1>(target)) EntityManager.RemoveComponent<NodeAssetRestrictionV1>(target);
        if (EntityManager.HasComponent<SegmentAssetRestrictionV1>(target)) EntityManager.RemoveComponent<SegmentAssetRestrictionV1>(target);
        if (EntityManager.HasBuffer<RestrictedVehicleAssetV1>(target)) EntityManager.RemoveComponent<RestrictedVehicleAssetV1>(target);
        MarkTargetLanesUpdated(target);
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
}
