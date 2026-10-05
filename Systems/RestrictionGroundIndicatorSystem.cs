using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;
namespace RouteFilter.Systems;
// No persistent ground stripes. Geometry exists only during range selection.
public sealed class RestrictionGroundIndicatorSystem : GameSystemBase
{
    private Material m_PreviewMaterial;
    private readonly List<(GameObject root,Mesh mesh)> m_Preview = new();
    public int Revision { get; private set; }
    public void MarkDirty(Entity target) { Revision++; }
    public void ResetRuntimeState() { ClearBrushPreview(); Revision++; }
    public void ClearRuntimeVisuals() { ClearBrushPreview(); Enabled = false; }
    protected override void OnGamePreload(Purpose purpose,GameMode mode) { ResetRuntimeState(); base.OnGamePreload(purpose,mode); }
    protected override void OnDestroy() { ClearBrushPreview(); Object.Destroy(m_PreviewMaterial); base.OnDestroy(); }
    protected override void OnUpdate() { }
    public void ClearBrushPreview() { foreach (var item in m_Preview) { Object.Destroy(item.root); Object.Destroy(item.mesh); } m_Preview.Clear(); }
    internal void PreviewRange(IEnumerable<Entity> targets)
    {
        ClearBrushPreview();
        if (m_PreviewMaterial == null)
        {
            var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color"); if (shader == null) return;
            m_PreviewMaterial = new Material(shader) { name = "RF.RangePreview" };
            var color = new Color(.15f,.8f,1f);
            if (m_PreviewMaterial.HasProperty("_UnlitColor")) m_PreviewMaterial.SetColor("_UnlitColor",color);
            if (m_PreviewMaterial.HasProperty("_Color")) m_PreviewMaterial.SetColor("_Color",color);
            if (m_PreviewMaterial.HasProperty("_CullMode")) m_PreviewMaterial.SetFloat("_CullMode",0);
        }
        var vertices = new List<Vector3>(); var indices = new List<int>();
        void Flush()
        {
            if (vertices.Count == 0) return;
            var mesh = new Mesh { name="RF.RangePreview",vertices=vertices.ToArray(),triangles=indices.ToArray() }; mesh.RecalculateBounds();
            m_Preview.Add((RuntimeSignResources.MeshObject(mesh.name,mesh,new[] {m_PreviewMaterial},null),mesh));
            vertices.Clear(); indices.Clear();
        }
        foreach (var target in targets)
        {
            if (!EntityManager.TryGetComponent(target,out Curve curve)) continue;
            if (vertices.Count >= 48000) Flush();
            for (int i=0;i<12;i++)
            {
                var start=Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,i/12f)+new float3(0,.12f,0);
                var end=Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,(i+1)/12f)+new float3(0,.12f,0);
                var side=math.normalizesafe(math.cross(end-start,math.up()))*.35f; var v=vertices.Count;
                vertices.Add(start-side);vertices.Add(start+side);vertices.Add(end+side);vertices.Add(end-side);
                indices.AddRange(new[] {v,v+1,v+2,v,v+2,v+3});
            }
        }
        Flush();
    }
}
