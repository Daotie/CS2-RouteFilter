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

    private struct DrawBadgeJob : IJob
    {
        public OverlayRenderSystem.Buffer Buffer;
        public float3 Position;
        public bool Prominent;

        public void Execute()
        {
            var alpha = Prominent ? 1f : 0.62f;
            var outline = new Color(0.92f, 0.16f, 0.10f, alpha);
            var fill = new Color(0.92f, 0.16f, 0.10f, alpha * (Prominent ? 0.82f : 0.32f));
            var bar = new Color(1f, 1f, 1f, alpha);
            var diameter = Prominent ? kBadgeDiameterProminent : kBadgeDiameter;

            // Flat tag: a horizontal prohibition disc floating above the target, sized
            // close to the selection highlight. It does not rotate with the camera.
            Buffer.DrawCircle(outline, fill, Prominent ? 0.9f : 0.8f, 0,
                new float2(0f, 1f), Position, diameter);

            var half = diameter * 0.32f;
            var barWidth = Prominent ? 1.7f : 1.4f;
            var line = new Colossal.Mathematics.Line3.Segment(
                Position + new float3(-half, 0f, half),
                Position + new float3(half, 0f, -half));
            Buffer.DrawLine(bar, line, barWidth, false);
        }
    }

    private ToolSystem m_ToolSystem = null!;
    private RestrictionToolSystem m_Tool = null!;
    private OverlayRenderSystem m_Overlay = null!;
    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;

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
    }

    protected override void OnUpdate()
    {
        if (m_ToolSystem.activeTool != m_Tool) return;
        if (m_Tool.SelectedTarget == m_Tool.HoveredTarget)
            Draw(m_Tool.SelectedTarget, true);
        else
        {
            Draw(m_Tool.HoveredTarget, false);
            Draw(m_Tool.SelectedTarget, true);
        }
        DrawRestrictionBadges();
        if (m_Tool.SelectedTarget != m_Tool.HoveredTarget)
        {
            DrawBadge(m_Tool.HoveredTarget, true);
            DrawBadge(m_Tool.SelectedTarget, true);
        }
        else
        {
            DrawBadge(m_Tool.HoveredTarget, true);
        }
    }

    private void DrawRestrictionBadges()
    {
        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        foreach (var node in nodes) DrawBadge(node, false);

        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        foreach (var segment in segments) DrawBadge(segment, false);
    }

    private void DrawBadge(Unity.Entities.Entity target, bool prominent)
    {
        if (target == Unity.Entities.Entity.Null || !HasActiveRestriction(target)) return;
        if (!TryGetBadgePosition(target, out var position)) return;

        var buffer = m_Overlay.GetBuffer(out var dependencies);
        var job = new DrawBadgeJob
        {
            Buffer = buffer,
            Position = position + new float3(0f, kBadgeHeight, 0f),
            Prominent = prominent
        };
        Dependency = job.Schedule(JobHandle.CombineDependencies(Dependency, dependencies));
        m_Overlay.AddBufferWriter(Dependency);
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
