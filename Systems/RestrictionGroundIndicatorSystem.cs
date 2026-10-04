using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RouteFilter.Systems;

// Persistent world meshes replace the old overlay job and its per-frame restriction queries.
public sealed class RestrictionGroundIndicatorSystem : GameSystemBase
{
    private readonly Dictionary<Entity, (GameObject root, Mesh mesh)> m_Indicators = new();
    private readonly HashSet<Entity> m_Dirty = new();
    private readonly Dictionary<Entity,uint> m_Stamps = new();
    private EntityQuery m_Targets, m_Changed;
    private Material m_Material;
    private Material m_PreviewMaterial;
    private readonly List<(GameObject root,Mesh mesh)> m_Preview = new();
    private RestrictionIndexSystem m_Index;
    private int m_IndexRevision = -1;
    private bool m_Enabled, m_AllDirty = true, m_Reset;
    public int Revision { get; private set; }
    public void MarkDirty(Entity target) { m_Dirty.Add(target); Revision++; }
    public void ResetRuntimeState() { m_Reset = true; Revision++; }
    protected override void OnCreate()
    {
        base.OnCreate(); m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_Targets = GetEntityQuery(ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_Changed = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<RestrictedVehicleAssetV1>() },
            Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() } });
    }
    protected override void OnDestroy() { Clear(); Object.Destroy(m_Material); Object.Destroy(m_PreviewMaterial); base.OnDestroy(); }
    protected override void OnGamePreload(Purpose purpose, GameMode mode)
    {
        Clear(); m_Dirty.Clear(); m_IndexRevision = -1; m_AllDirty = true; Revision++;
        base.OnGamePreload(purpose,mode);
    }
    public void ClearRuntimeVisuals() { Clear(); m_Dirty.Clear(); m_AllDirty = true; Enabled = false; }
    public void ClearBrushPreview() { foreach (var item in m_Preview) { Object.Destroy(item.root); Object.Destroy(item.mesh); } m_Preview.Clear(); }
    internal void PreviewBrushTarget(Entity target)
    {
        if (!EntityManager.TryGetComponent(target,out Curve curve)) return;
        if (m_PreviewMaterial == null)
        {
            var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color"); if (shader == null) return;
            m_PreviewMaterial = new Material(shader) { name = "RF.BrushPreview" };
            var color = new Color(.15f,.8f,1f);
            if (m_PreviewMaterial.HasProperty("_UnlitColor")) m_PreviewMaterial.SetColor("_UnlitColor",color);
            if (m_PreviewMaterial.HasProperty("_Color")) m_PreviewMaterial.SetColor("_Color",color);
            if (m_PreviewMaterial.HasProperty("_CullMode")) m_PreviewMaterial.SetFloat("_CullMode",0);
        }
        var vertices = new Vector3[48]; var indices = new int[72];
        for (int i = 0; i < 12; i++)
        {
            var start = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,i/12f) + new float3(0,.12f,0);
            var end = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,(i+1)/12f) + new float3(0,.12f,0);
            var side = math.normalizesafe(math.cross(end-start,math.up()))*.35f; int v = i*4, t = i*6;
            vertices[v] = start-side; vertices[v+1] = start+side; vertices[v+2] = end+side; vertices[v+3] = end-side;
            indices[t] = v; indices[t+1] = v+1; indices[t+2] = v+2; indices[t+3] = v; indices[t+4] = v+2; indices[t+5] = v+3;
        }
        var mesh = new Mesh { name = "RF.Brush:" + target, vertices = vertices, triangles = indices }; mesh.RecalculateBounds();
        m_Preview.Add((RuntimeSignResources.MeshObject(mesh.name,mesh,new[] { m_PreviewMaterial },null),mesh));
    }
    private void Clear() { ClearBrushPreview(); foreach (var item in m_Indicators.Values) { Object.Destroy(item.root); Object.Destroy(item.mesh); } m_Indicators.Clear(); m_Stamps.Clear(); }
    protected override void OnUpdate()
    {
        if (m_Reset) { m_Reset = false; Clear(); m_Dirty.Clear(); m_AllDirty = true; }
        if (m_IndexRevision != m_Index.Revision) { m_IndexRevision = m_Index.Revision; m_AllDirty = true; Revision++; }
        var enabled = Mod.Settings.EnableRestrictionBadges;
        if (enabled != m_Enabled) { m_Enabled = enabled; m_AllDirty = true; if (!enabled) Clear(); }
        if (!m_Changed.IsEmptyIgnoreFilter)
        {
            using var changed = m_Changed.ToEntityArray(Allocator.Temp);
            foreach (var target in changed)
            {
                var stamp = VisualGeometryStamp.Read(EntityManager,target);
                if (!m_Stamps.TryGetValue(target,out var previous) || stamp != previous) MarkDirty(target);
                m_Stamps[target] = stamp;
            }
        }
        if (!enabled) { m_Dirty.Clear(); return; }
        if (m_AllDirty)
        {
            m_AllDirty = false;
            foreach (var target in m_Stamps.Keys.ToArray()) if (!EntityManager.Exists(target)) m_Stamps.Remove(target);
            foreach (var target in m_Indicators.Keys) m_Dirty.Add(target);
            using var targets = m_Targets.ToEntityArray(Allocator.Temp);
            foreach (var target in targets) m_Dirty.Add(target);
        }
        if (m_Dirty.Count == 0) return;
        var dirty = m_Dirty.ToArray(); m_Dirty.Clear();
        foreach (var target in dirty) Rebuild(target);
    }
    private void Rebuild(Entity target)
    {
        m_Stamps[target] = VisualGeometryStamp.Read(EntityManager,target);
        if (m_Indicators.TryGetValue(target,out var old)) { Object.Destroy(old.root); Object.Destroy(old.mesh); m_Indicators.Remove(target); }
        if (!EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target) ||
            !EntityManager.TryGetBuffer(target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets) || assets.Length == 0) return;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        void Line(float3 start, float3 end)
        {
            var side = math.normalizesafe(math.cross(end-start,math.up())) * .12f;
            if (math.lengthsq(side) < .001f) return;
            start.y += .06f; end.y += .06f; int i = vertices.Count;
            vertices.Add(start-side); vertices.Add(start+side); vertices.Add(end+side); vertices.Add(end-side);
            triangles.AddRange(new[] { i,i+1,i+2,i,i+2,i+3 });
        }
        var entries = m_Index.GetAppliedRoadEntries(target);
        foreach (var entry in entries)
            if (entry.Enabled && m_Index.TryGetApproachGeometry(entry,out var start,out var end)) Line(start,end);
        if (entries.Count == 0 && EntityManager.TryGetComponent(target,out Curve curve))
            for (int i = 0; i < 12; i++) Line(Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,i/12f),Colossal.Mathematics.MathUtils.Position(curve.m_Bezier,(i+1)/12f));
        if (vertices.Count == 0) return;
        if (m_Material == null)
        {
            var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            m_Material = new Material(shader) { name = "RF.GroundIndicator" };
            if (m_Material.HasProperty("_UnlitColor")) m_Material.SetColor("_UnlitColor",new Color(.9f,.14f,.08f));
            if (m_Material.HasProperty("_Color")) m_Material.SetColor("_Color",new Color(.9f,.14f,.08f));
            if (m_Material.HasProperty("_CullMode")) m_Material.SetFloat("_CullMode",0);
        }
        var mesh = new Mesh { name = "RF.Ground:" + target, vertices = vertices.ToArray(), triangles = triangles.ToArray() }; mesh.RecalculateBounds();
        var root = RuntimeSignResources.MeshObject(mesh.name,mesh,new[] { m_Material },null);
        m_Indicators[target] = (root,mesh);
    }
}
