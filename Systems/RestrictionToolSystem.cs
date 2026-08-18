using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using NodeGate.Components;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetSubLane = Game.Net.SubLane;

namespace NodeGate.Systems;

public sealed partial class RestrictionToolSystem : ToolBaseSystem
{
    public override string toolID => "NodeGateTool";
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

        var node = ResolveNode(entity, hit.m_HitPosition);
        m_ToolSystem.selected = node;
        if (node == Entity.Null)
            return inputDeps;

        if (Mod.Apply.WasPressedThisFrame())
            SetRestriction(node, Mod.SelectedVehicleTypes);
        else if (Mod.Clear.WasPressedThisFrame())
            SetRestriction(node, VehicleTypeMask.None);

        return inputDeps;
    }

    private Entity ResolveNode(Entity entity, float3 hitPosition)
    {
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

    public void SetRestriction(Entity node, VehicleTypeMask vehicleTypes)
    {
        vehicleTypes &= VehicleTypeMask.All;
        var rules = Mod.Settings.GetNativeRules(vehicleTypes);
        rules &= NodeVehicleRestriction.SupportedRules;
        if (vehicleTypes == VehicleTypeMask.None)
        {
            if (EntityManager.HasComponent<NodeVehicleRestriction>(node))
                EntityManager.RemoveComponent<NodeVehicleRestriction>(node);
        }
        else if (EntityManager.HasComponent<NodeVehicleRestriction>(node))
        {
            EntityManager.SetComponentData(node, new NodeVehicleRestriction(vehicleTypes, rules));
        }
        else
        {
            EntityManager.AddComponentData(node, new NodeVehicleRestriction(vehicleTypes, rules));
        }

        if (EntityManager.TryGetBuffer(node, true, out DynamicBuffer<NetSubLane> lanes))
        {
            foreach (var subLane in lanes)
            {
                if (!EntityManager.HasComponent<Updated>(subLane.m_SubLane))
                    EntityManager.AddComponent<Updated>(subLane.m_SubLane);
            }
        }

        Mod.Log.Info($"Node {node.Index}:{node.Version} restrictions set to {vehicleTypes} (native rules: {rules})");
    }
}
