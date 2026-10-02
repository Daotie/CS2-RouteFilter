using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Net;
using Game.Pathfind;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace RouteFilter.Systems;

public sealed partial class RestrictionOverlaySystem
{
    // Read-only visual sample, not enforcement. Bound both collection and drawing.
    private const int RouteLaneLimit = 128;
    private const int RouteObjectLimit = 256;
    private const int RouteVehicleLimit = 32;
    private const int RouteElementLimit = 256;
    private const int RouteCurveLimit = 1024;
    private NativeList<Bezier4x3> m_RouteCurves;
    private JobHandle m_RouteJob;
    private Entity m_RouteTarget;
    private int m_RouteRefreshFrame;
    private readonly HashSet<Entity> m_RouteTargetLanes = new();
    private readonly HashSet<Entity> m_RouteSourceLanes = new();
    private readonly HashSet<Entity> m_RouteVehicles = new();
    private readonly HashSet<Entity> m_RouteDrawnLanes = new();

    private struct DrawRoutesJob : IJob
    {
        public OverlayRenderSystem.Buffer Buffer;
        [ReadOnly] public NativeArray<Bezier4x3> Curves;
        public void Execute()
        {
            foreach (var curve in Curves)
                Buffer.DrawDashedCurve(new Color(.35f, .82f, 1f, .65f), curve, 1.2f, 14f, .6f);
        }
    }

    private void InitializeRoutePreview() => m_RouteCurves = new NativeList<Bezier4x3>(128, Allocator.Persistent);
    private void DisposeRoutePreview()
    {
        m_RouteJob.Complete();
        m_RouteCurves.Dispose();
    }
    private void ClearRoutePreview()
    {
        m_RouteJob.Complete();
        m_RouteCurves.Clear();
        m_RouteTarget = Entity.Null;
        m_RouteRefreshFrame = 0;
        m_RouteTargetLanes.Clear();
        m_RouteSourceLanes.Clear();
        m_RouteVehicles.Clear();
        m_RouteDrawnLanes.Clear();
    }

    private void DrawRoutePreview()
    {
        var target = m_Tool.SelectedTarget;
        m_RouteJob.Complete();
        if (target == Entity.Null || !EntityManager.Exists(target))
        {
            if (m_RouteTarget != Entity.Null) ClearRoutePreview();
            return;
        }
        if (target != m_RouteTarget || UnityEngine.Time.frameCount - m_RouteRefreshFrame >= 30)
        {
            CollectRoutePreview(target);
            m_RouteTarget = target;
            m_RouteRefreshFrame = UnityEngine.Time.frameCount;
        }
        if (m_RouteCurves.Length == 0) return;
        var buffer = m_Overlay.GetBuffer(out var dependencies);
        m_RouteJob = new DrawRoutesJob { Buffer = buffer, Curves = m_RouteCurves.AsArray() }
            .Schedule(JobHandle.CombineDependencies(Dependency, dependencies));
        Dependency = m_RouteJob;
        m_Overlay.AddBufferWriter(m_RouteJob);
    }

    private void CollectRoutePreview(Entity target)
    {
        m_RouteCurves.Clear();
        m_RouteTargetLanes.Clear();
        m_RouteSourceLanes.Clear();
        m_RouteVehicles.Clear();
        m_RouteDrawnLanes.Clear();
        AddPreviewLanes(target, true);
        // Vehicles approaching a node occupy its adjacent edge lanes before
        // entering the node's internal lanes. Sample these, then require their
        // saved path to actually reference the selected node or its own lanes.
        if (EntityManager.TryGetBuffer(target, true, out DynamicBuffer<ConnectedEdge> edges))
            for (var i = 0; i < edges.Length && i < RouteLaneLimit && m_RouteSourceLanes.Count < RouteLaneLimit; i++)
                AddPreviewLanes(edges[i].m_Edge, false);

        var objectsChecked = 0;
        foreach (var lane in m_RouteSourceLanes)
        {
            if (!EntityManager.TryGetBuffer(lane, true, out DynamicBuffer<LaneObject> objects)) continue;
            for (var i = 0; i < objects.Length && objectsChecked < RouteObjectLimit; i++, objectsChecked++)
            {
                var vehicle = objects[i].m_LaneObject;
                if (EntityManager.TryGetComponent(vehicle, out Game.Vehicles.Controller controller))
                    vehicle = controller.m_Controller;
                if (EntityManager.Exists(vehicle)
                    && !EntityManager.HasComponent<Game.Common.Deleted>(vehicle)
                    && !EntityManager.HasComponent<Game.Tools.Temp>(vehicle)
                    && EntityManager.HasBuffer<PathElement>(vehicle))
                    m_RouteVehicles.Add(vehicle);
                if (m_RouteVehicles.Count >= RouteVehicleLimit) break;
            }
            if (objectsChecked >= RouteObjectLimit || m_RouteVehicles.Count >= RouteVehicleLimit) break;
        }
        foreach (var vehicle in m_RouteVehicles)
        {
            var path = EntityManager.GetBuffer<PathElement>(vehicle, true);
            var start = EntityManager.TryGetComponent(vehicle, out PathOwner owner)
                ? System.Math.Max(0, owner.m_ElementIndex) : 0;
            var end = System.Math.Min(path.Length, start + RouteElementLimit);
            var relevant = false;
            for (var i = start; i < end; i++)
                if (path[i].m_Target == target || m_RouteTargetLanes.Contains(path[i].m_Target)) { relevant = true; break; }
            if (!relevant) continue;
            for (var i = start; i < end && m_RouteCurves.Length < RouteCurveLimit; i++)
            {
                var lane = path[i].m_Target;
                if (m_RouteDrawnLanes.Add(lane) && EntityManager.TryGetComponent(lane, out Curve curve))
                    m_RouteCurves.Add(curve.m_Bezier);
            }
            if (m_RouteCurves.Length >= RouteCurveLimit) break;
        }
    }

    private void AddPreviewLanes(Entity target, bool selected)
    {
        if (!selected && m_RouteSourceLanes.Count >= RouteLaneLimit) return;
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<SubLane> lanes)) return;
        for (var i = 0; i < lanes.Length && i < RouteLaneLimit; i++)
        {
            var lane = lanes[i].m_SubLane;
            if (selected) m_RouteTargetLanes.Add(lane);
            if (m_RouteSourceLanes.Count < RouteLaneLimit) m_RouteSourceLanes.Add(lane);
        }
    }
}
