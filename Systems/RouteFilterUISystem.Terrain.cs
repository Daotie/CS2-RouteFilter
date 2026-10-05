using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game.Simulation;
using Unity.Mathematics;
namespace RouteFilter.Systems;
public sealed partial class RouteFilterUISystem
{
    private ValueBinding<string> m_MapTerrain;
    private float m_NextTerrainSample;
    private void UpdateMapTerrain()
    {
        // No entity scan; sample a bounded coarse texture grid only while open.
        if (!m_MapOpen || UnityEngine.Time.unscaledTime < m_NextTerrainSample) return;
        m_NextTerrainSample = UnityEngine.Time.unscaledTime + 3f;
        var heights = World.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData();
        if (!heights.isCreated) return;
        var water = World.GetOrCreateSystemManaged<WaterSystem>().GetSurfaceData(out var dependencies);
        dependencies.Complete();
        if (!water.isCreated) return;
        var minimum = TerrainUtils.ToWorldSpace(ref heights, float3.zero);
        var maximum = TerrainUtils.ToWorldSpace(ref heights, new float3(heights.resolution.x, 0, heights.resolution.z));
        if (!math.all(math.isfinite(minimum)) || !math.all(math.isfinite(maximum))) return;
        const int cells = 128;
        var cell = (maximum.xz - minimum.xz) / cells;
        if (cell.x <= 0 || cell.y <= 0) return;
        var payload = new StringBuilder();
        void Point(float x, float z) => payload.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(z.ToString("0.##", CultureInfo.InvariantCulture));
        void Rectangle(string type, float x, float z, float width, float height)
        {
            payload.Append(type).Append("||");Point(x,z);payload.Append(';');Point(x+width,z);payload.Append(';');Point(x+width,z+height);payload.Append(';');Point(x,z+height);payload.Append(';');Point(x,z);payload.Append('\n');
        }
        Rectangle("W",minimum.x,minimum.z,maximum.x-minimum.x,maximum.z-minimum.z);
        int runs=0;
        for(int row=0;row<cells;row++)
        {
            int start=-1;
            for(int column=0;column<=cells;column++)
            {
                bool land=false;
                if(column<cells)
                {
                    var point = new float3(minimum.x+(column+.5f)*cell.x,0,minimum.z+(row+.5f)*cell.y);
                    var height = TerrainUtils.SampleHeight(ref heights, point);
                    var depth = WaterUtils.SampleDepth(ref water,point);
                    land=math.isfinite(height) && math.isfinite(depth) && depth<=.5f;
                }
                if(land && start<0) start=column;
                if(!land && start>=0)
                {
                    Rectangle("L",minimum.x+start*cell.x,minimum.z+row*cell.y,(column-start)*cell.x,cell.y);
                    start=-1;runs++;
                }
            }
        }
        m_MapTerrain.Update(payload.ToString());
        m_NextTerrainSample = UnityEngine.Time.unscaledTime + 60f;
        Mod.Log.Info($"[RouteFilter.Map.Terrain] cells={cells} runs={runs} bounds={minimum.xz}..{maximum.xz} cachedSeconds=60");
    }
}
