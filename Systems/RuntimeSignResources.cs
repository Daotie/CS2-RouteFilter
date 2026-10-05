using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;
using Game.Prefabs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RouteFilter.Systems;

// Runtime-only rendering resources, shared across targets and destroyed with their world.
internal sealed class RuntimeSignResources : IDisposable
{
    private Mesh m_Plate, m_TextQuad;
    private Material m_PlateMaterial;
    private readonly Dictionary<string, Material> m_Text = new(StringComparer.Ordinal);
    private readonly List<Texture2D> m_Textures = new();
    private readonly Dictionary<string, List<(Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation)>> m_Main = new();
    private bool m_PlateAttempted;
    private readonly HashSet<string> m_FailedMain = new(StringComparer.Ordinal);
    private static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream("RouteFilter." + name)
        ?? throw new FileNotFoundException("Missing embedded sign resource: " + name);
    private static Shader SignShader() => Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture")
        ?? throw new InvalidOperationException("No supported sign shader");
    private Texture2D Texture(string name, bool linear = false)
    {
        using var input = Resource(name); using var bytes = new MemoryStream(); input.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear) { name = "RF:" + name };
        m_Textures.Add(texture);
        if (!ImageConversion.LoadImage(texture, bytes.ToArray())) throw new InvalidDataException(name);
        return texture;
    }
    private static void SetTexture(Material material, Texture texture)
    {
        if (material.HasProperty("_UnlitColorMap")) { material.SetTexture("_UnlitColorMap", texture); material.EnableKeyword("_UNLIT_COLOR_MAP"); }
        if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", Color.white);
        if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", 0);
        if (material.HasProperty("_CullModeForward")) material.SetFloat("_CullModeForward", 0);
    }
    private void EnsurePlate()
    {
        if (m_PlateAttempted) return;
        m_PlateAttempted = true;
        using var reader = new BinaryReader(Resource("RF-Plate.mesh"));
        int count = reader.ReadInt32(), indexCount = reader.ReadInt32();
        var vertices = new Vector3[count]; var uv = new Vector2[count]; var indices = new int[indexCount];
        for (int i = 0; i < count; i++) { vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle()); }
        for (int i = 0; i < indexCount; i++) indices[i] = reader.ReadInt32();
        m_Plate = new Mesh { name = "RF-Plate", vertices = vertices, uv = uv, triangles = indices };
        m_Plate.RecalculateNormals(); m_Plate.RecalculateBounds();
        m_PlateMaterial = new Material(SignShader()) { name = "RF-Plate.Shared" };
        SetTexture(m_PlateMaterial, Texture("RF-Plate_BaseColor.png"));
        var mask = Texture("RF-Plate_MaskMap.png", true);
        var control = Texture("RF-Plate_ControlMask.png", true);
        if (m_PlateMaterial.HasProperty("_MaskMap")) m_PlateMaterial.SetTexture("_MaskMap", mask);
        if (m_PlateMaterial.HasProperty("_ControlMask")) m_PlateMaterial.SetTexture("_ControlMask", control);
        // Main signs face local +Z (LookRotation(-travel)). The plate text uses
        // that same front, clockwise-from-front winding and upright UVs.
        m_TextQuad = new Mesh { name = "RF.TextQuad", vertices = new[] { new Vector3(.375f,-.102f,.012f), new Vector3(-.375f,-.102f,.012f), new Vector3(-.375f,.102f,.012f), new Vector3(.375f,.102f,.012f) },
            uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) }, triangles = new[] { 0,2,1,0,3,2 } };
        m_TextQuad.RecalculateNormals(); m_TextQuad.RecalculateBounds();
    }
    private Material TextMaterial(RouteFilter.Persistence.TrafficLegend legend,string locale,string profile)
    {
        var language=RouteFilter.Persistence.TrafficSignLocalization.Language(locale);
        var dictionary=Game.SceneFlow.GameManager.instance.localizationManager.activeDictionary;
        var text=RouteFilter.Persistence.TrafficSignLocalization.ResolveText(legend,locale,key=>dictionary.TryGetValue(key,out var value)?value:null);
        var key=language+"|"+legend.Key+"|"+profile+"|"+SignTextLayout.FontStyleKey(language)+"|"+SignTextLayout.Tier(text)+"|"+text;
        if (m_Text.TryGetValue(key, out var cached)) return cached;
        using var bitmap = new System.Drawing.Bitmap(SignTextLayout.Width,SignTextLayout.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) SignTextLayout.Draw(graphics,text,language);
        using var output = new MemoryStream(); bitmap.Save(output, ImageFormat.Png);
        var texture = new Texture2D(2,2,TextureFormat.RGBA32,false) { name = "RF.Label:" + text };
        m_Textures.Add(texture); ImageConversion.LoadImage(texture, output.ToArray());
        var material = new Material(SignShader()) { name = "RF.Label:" + text };
        SetTexture(material, texture);
        if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode",2);
        if (material.HasProperty("_CullModeForward")) material.SetFloat("_CullModeForward",2);
        m_Text[key] = material; return material;
    }
    internal static GameObject MeshObject(string name, Mesh mesh, Material[] materials, Transform parent)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.DontSave };
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return obj;
    }
    internal void Plate(Transform parent, float height, RouteFilter.Persistence.TrafficLegend legend,string locale,string profile)
    {
        EnsurePlate();
        if (m_PlateMaterial == null || m_TextQuad == null) throw new InvalidOperationException("RF-Plate resources unavailable");
        var plate = MeshObject("RF-Plate", m_Plate, new[] { m_PlateMaterial }, parent);
        plate.transform.localPosition = new Vector3(0,height,0);
        MeshObject("RF-Plate.Text", m_TextQuad, new[] { TextMaterial(legend,locale,profile) }, plate.transform);
    }
    internal void ScaledMain(Transform parent, StaticObjectPrefab prefab, float scale, Game.Objects.ObjectState state)
    {
        // Separate assembly transform; shared vanilla meshes/materials are never modified.
        var key = prefab.name + ":" + (int)state;
        if (m_FailedMain.Contains(key)) throw new InvalidOperationException("Scaled prefab unavailable: " + prefab.name);
        if (!m_Main.TryGetValue(key, out var meshes))
        {
            meshes = new();
            try
            {
            foreach (var info in prefab.m_Meshes)
            {
                if ((info.m_RequireState & state) != info.m_RequireState) continue;
                if (info.m_Mesh is not RenderPrefab render) continue;
                var materials = render.ObtainMaterials(false);
                for (int i = 0; i < materials.Length; i++)
                {
                    var mesh = render.ObtainMesh(i, out var subMesh);
                    // Preserve the authored submesh selection without modifying the asset.
                    var copy = Object.Instantiate(mesh); copy.subMeshCount = 1; copy.SetTriangles(mesh.GetTriangles(subMesh),0);
                    meshes.Add((copy, new[] { materials[i] }, info.m_Position, info.m_Rotation));
                }
            }
            if (meshes.Count == 0) throw new InvalidOperationException("No vanilla mesh for scaled assembly");
            m_Main[key] = meshes;
            }
            catch { foreach (var info in meshes) Object.Destroy(info.mesh); m_FailedMain.Add(key); throw; }
        }
        var root = new GameObject("RF.MainScale") { hideFlags = HideFlags.DontSave }; root.transform.SetParent(parent,false); root.transform.localScale = Vector3.one * scale;
        try
        {
            foreach (var info in meshes)
            {
                var obj = MeshObject("RF.Main", info.mesh, info.materials, root.transform);
                obj.transform.localPosition = info.position; obj.transform.localRotation = info.rotation;
            }
        }
        catch { Object.Destroy(root); throw; }
    }
    public void Dispose()
    {
        Object.Destroy(m_Plate); Object.Destroy(m_TextQuad); Object.Destroy(m_PlateMaterial);
        foreach (var material in m_Text.Values) Object.Destroy(material);
        foreach (var texture in m_Textures) Object.Destroy(texture);
        foreach (var meshes in m_Main.Values) foreach (var info in meshes) Object.Destroy(info.mesh);
        m_Main.Clear(); m_Text.Clear(); m_Textures.Clear(); m_FailedMain.Clear();
    }
}
