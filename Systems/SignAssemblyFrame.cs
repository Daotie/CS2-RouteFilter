using Unity.Mathematics;
namespace RouteFilter.Systems;
// Native marker local +Z faces -approach; local right comes from the same positive rotation.
// A plate's authored/text front is +Z at +12mm. Align that front with the primary's
// foremost native bounds plane, adding one authored plate thickness of clearance.
internal static class SignAssemblyFrame
{
    internal static bool TryPlateAnchor(float3 min,float3 max,float scale,float height,out float3 anchor)
    {
        anchor=default;
        if(!math.all(math.isfinite(min)) || !math.all(math.isfinite(max)) || !math.isfinite(scale) || !math.isfinite(height) || scale<=0 || math.any(max<min)) return false;
        anchor=new float3((min.x+max.x)*.5f*scale,height,max.z*scale+.020f-.012f);
        return math.all(math.isfinite(anchor));
    }
}
