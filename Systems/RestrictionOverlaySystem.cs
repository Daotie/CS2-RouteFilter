using Game;
using Colossal.Entities;
using Game.Net;
using Game.Rendering;
using Game.Tools;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RouteFilter.Systems;

public sealed partial class RestrictionOverlaySystem : GameSystemBase
{
    private const float kBadgeHeight = 2.8f;
    private const float kBadgeDiameter = 12f;
    private const float kBadgeDiameterProminent = 13f;

    private struct DrawTargetJob : IJob
    {
        public OverlayRenderSystem.Buffer Buffer;
        public bool IsNode;
        public float3 Position;
        public Colossal.Mathematics.Bezier4x3 Curve;
        public bool Selected;

        public void Execute()
        {
            var outline = Selected ? new Color(0.18f, 0.82f, 1f, 1f) : new Color(1f, 0.34f, 0.22f, 0.98f);
            var fill = Selected ? new Color(0.1f, 0.72f, 1f, 0.11f) : new Color(1f, 0.18f, 0.12f, 0.06f);
            if (IsNode)
            {
                Buffer.DrawCircle(outline, fill, Selected ? 1.05f : 0.75f,
                    OverlayRenderSystem.StyleFlags.Projected | OverlayRenderSystem.StyleFlags.DepthFadeBelow,
                    new float2(0f, 1f), Position, 13f);
            }
            else
            {
                Buffer.DrawDashedCurve(outline, Curve, Selected ? 4f : 2.6f, Selected ? 1.2f : 3.5f, 2f);
            }
        }
    }

    private struct DrawBadgesJob : IJobParallelFor
    {
        public OverlayRenderSystem.Buffer Buffer;
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<bool> Prominent;

        public void Execute(int index)
        {
            var isProminent = Prominent[index];
            var alpha = isProminent ? 1f : 0.62f;
            var outline = new Color(0.92f, 0.16f, 0.10f, alpha);
            var fill = new Color(0.92f, 0.16f, 0.10f, alpha * (isProminent ? 0.82f : 0.32f));
            var bar = new Color(1f, 1f, 1f, alpha);
            var diameter = isProminent ? kBadgeDiameterProminent : kBadgeDiameter;

            // Flat tag: a horizontal prohibition disc floating above the target, sized
            // close to the selection highlight. It does not rotate with the camera.
            Buffer.DrawCircle(outline, fill, isProminent ? 0.9f : 0.8f, 0,
                new float2(0f, 1f), Positions[index], diameter);

            var half = diameter * 0.32f;
            var barWidth = isProminent ? 1.7f : 1.4f;
            var line = new Colossal.Mathematics.Line3.Segment(
                Positions[index] + new float3(-half, 0f, half),
                Positions[index] + new float3(half, 0f, -half));
            Buffer.DrawLine(bar, line, barWidth, false);
        }
    }

    private ToolSystem m_ToolSystem = null!;
    private RestrictionToolSystem m_Tool = null!;
    private OverlayRenderSystem m_Overlay = null!;
    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    // Persistent scratch arrays for the batched badge job; reused every frame so the job
    // pipeline stays allocation free and the previous job is completed before refilling.
    private NativeList<float3> m_BadgePositions;
    private NativeList<bool> m_BadgeProminent;
    private JobHandle m_BadgeJobHandle;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        m_Tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<Game.Net.Edge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_BadgePositions = new NativeList<float3>(64, Allocator.Persistent);
        m_BadgeProminent = new NativeList<bool>(64, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        m_BadgeJobHandle.Complete();
        m_BadgePositions.Dispose();
        m_BadgeProminent.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (m_ToolSystem.activeTool != m_Tool) return;

        // The badge job from the previous frame reads the scratch arrays; finish it before
        // refilling them so they are never overwritten while a worker still uses them.
        m_BadgeJobHandle.Complete();

        if (m_Tool.SelectedTarget == m_Tool.HoveredTarget)
            Draw(m_Tool.SelectedTarget, true);
        else
        {
            Draw(m_Tool.HoveredTarget, false);
            Draw(m_Tool.SelectedTarget, true);
        }
        DrawRestrictionBadges();
    }

    private void DrawRestrictionBadges()
    {
        m_BadgePositions.Clear();
        m_BadgeProminent.Clear();

        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        foreach (var node in nodes) AddBadge(node);

        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        foreach (var segment in segments) AddBadge(segment);

        if (m_BadgePositions.Length == 0) return;

        var buffer = m_Overlay.GetBuffer(out var dependencies);
        var job = new DrawBadgesJob
        {
            Buffer = buffer,
            Positions = m_BadgePositions.AsArray(),
            Prominent = m_BadgeProminent.AsArray()
        };
        m_BadgeJobHandle = job.Schedule(m_BadgePositions.Length, 16, JobHandle.CombineDependencies(Dependency, dependencies));
        Dependency = m_BadgeJobHandle;
        m_Overlay.AddBufferWriter(m_BadgeJobHandle);
    }

    private void AddBadge(Unity.Entities.Entity target)
    {
        if (target == Unity.Entities.Entity.Null || !HasActiveRestriction(target)) return;
        if (!TryGetBadgePosition(target, out var position)) return;

        m_BadgePositions.Add(position + new float3(0f, kBadgeHeight, 0f));
        m_BadgeProminent.Add(target == m_Tool.HoveredTarget || target == m_Tool.SelectedTarget);
    }

    private bool HasActiveRestriction(Unity.Entities.Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets)) return false;
        return assets.Length > 0;
    }

    private bool TryGetBadgePosition(Unity.Entities.Entity target, out float3 position)
    {
        if (EntityManager.TryGetComponent(target, out Node node))
        {
            position = node.m_Position;
            return true;
        }

        if (EntityManager.TryGetComponent(target, out Curve curve))
        {
            var bezier = curve.m_Bezier;
            position = (bezier.a + bezier.b * 3f + bezier.c * 3f + bezier.d) / 8f;
            return true;
        }

        position = default;
        return false;
    }

    private void Draw(Unity.Entities.Entity target, bool selected)
    {
        if (target == Unity.Entities.Entity.Null) return;

        var isNode = EntityManager.TryGetComponent(target, out Node node);
        var hasCurve = EntityManager.TryGetComponent(target, out Curve curve);
        if (!isNode && !hasCurve) return;

        var buffer = m_Overlay.GetBuffer(out var dependencies);
        var job = new DrawTargetJob
        {
            Buffer = buffer,
            IsNode = isNode,
            Position = isNode ? node.m_Position + new float3(0f, 0.35f, 0f) : default,
            Curve = hasCurve ? curve.m_Bezier : default,
            Selected = selected
        };
        Dependency = job.Schedule(JobHandle.CombineDependencies(Dependency, dependencies));
        m_Overlay.AddBufferWriter(Dependency);
    }
}
