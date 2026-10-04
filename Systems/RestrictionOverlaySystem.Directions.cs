using Colossal.Mathematics;
using Game.Rendering;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RouteFilter.Systems;

public sealed partial class RestrictionOverlaySystem
{
    // Copy cached scalar geometry into each job. No native container, allocation,
    // topology walk or synchronization is added to the rendering hot path.
    private struct DrawEntryJob : IJob
    {
        public OverlayRenderSystem.Buffer Buffer;
        public float3 Start, End;
        public bool Enabled, Supported;
        public void Execute()
        {
            var active = Enabled && Supported;
            var color = active ? new Color(.18f, .82f, 1f, .95f) : new Color(.66f, .74f, .78f, .4f);
            Buffer.DrawLine(color, new Line3.Segment(Start, End), 1.5f, false);
            Buffer.DrawCircle(color, new Color(color.r, color.g, color.b, active ? .8f : .15f), .35f,
                OverlayRenderSystem.StyleFlags.Projected | OverlayRenderSystem.StyleFlags.DepthFadeBelow,
                new float2(0f, 1f), (Start + End) * .5f, 2.8f);
        }
    }

    private void DrawEntryDirections()
    {
        var bars = m_Tool.EntryBars;
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var buffer = m_Overlay.GetBuffer(out var dependencies);
            var job = new DrawEntryJob { Buffer = buffer, Start = bar.Start, End = bar.End,
                Enabled = bar.Enabled, Supported = m_Tool.EntryDirectionsSupported };
            Dependency = job.Schedule(JobHandle.CombineDependencies(Dependency, dependencies));
            m_Overlay.AddBufferWriter(Dependency);
        }
    }
}
