using Game;
using Colossal.Entities;
using Game.Net;
using Game.Rendering;
using Game.Tools;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RouteFilter.Systems;

public sealed partial class RestrictionOverlaySystem : GameSystemBase
{
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

    private ToolSystem m_ToolSystem = null!;
    private RestrictionToolSystem m_Tool = null!;
    private OverlayRenderSystem m_Overlay = null!;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        m_Tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
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
