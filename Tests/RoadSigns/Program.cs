using RouteFilter.Systems;
using Unity.Mathematics;

var checks = 0;
void Check(bool valid, string description) { checks++; if (!valid) throw new Exception(description); }
bool Near(float3 a, float3 b) => math.distance(a, b) < .001f;
foreach (var direction in new[] { new float3(0, 0, 1), new float3(0, 0, -1), new float3(1, 0, 0), new float3(-1, 0, 0), math.normalize(new float3(1, 0, 1)) })
{
    var center = new float3(27, 5, -16);
    Check(RoadSignPlacement.TryCreate(center, direction, -1.75f, 1.75f, 0f, out var left, out var right, out var rotation), "single entering lane");
    var expectedLeft = new float3(-direction.z, 0, direction.x);
    Check(Near(left - center, expectedLeft * 2.55f), "left must follow travel direction");
    Check(Near(right - center, -expectedLeft * 2.55f), "right must follow travel direction");
    Check(Near(math.forward(rotation), -direction), "sign front faces approaching traffic");
    Check(RoadSignPlacement.TryCreate(center, direction, -8f, 11f, .3f, out left, out right, out rotation), "asymmetric multi-lane entry");
    Check(math.abs(math.dot(left - center, expectedLeft) - 11.8f) < .001f && math.abs(math.dot(right - center, expectedLeft) + 8.8f) < .001f, "actual outer bounds");
    Check(math.abs(left.y - 5.3f) < .001f && math.abs(right.y - 5.3f) < .001f, "ground offset");
}
Check(!RoadSignPlacement.TryCreate(default, default, -1, 1, 0, out _, out _, out _), "zero direction skipped");
Check(!RoadSignPlacement.TryCreate(new float3(float.NaN), new float3(0, 0, 1), -1, 1, 0, out _, out _, out _), "invalid position skipped");
Check(!RoadSignPlacement.TryCreate(default, new float3(0, 0, 1), 2, 1, 0, out _, out _, out _), "invalid bounds skipped");
Check(!RoadSignPlacement.HasMedian(-3,3,3,6,false),"painted divider is not a median");
Check(!RoadSignPlacement.HasMedian(-3,3,3.4f,6,false),"narrow painted gap is not a median");
Check(RoadSignPlacement.HasMedian(-3,3,5,8,false),"separated right-hand approach has median");
Check(RoadSignPlacement.HasMedian(-3,3,-8,-5,true),"separated left-hand approach has median");
float lm = .8f, rm = .8f;
RoadSignPlacement.ConstrainToDivider(-3, 3, 3, 6, ref lm, ref rm);
Check(lm == 0 && rm == .8f, "unseparated left divider: centered; sidewalk unchanged");
lm = rm = .8f;
RoadSignPlacement.ConstrainToDivider(-3, 3, -7, -3, ref lm, ref rm);
Check(rm == 0 && lm == .8f, "unseparated right divider: centered; sidewalk unchanged");
lm = rm = .8f;
RoadSignPlacement.ConstrainToDivider(-3, 3, 7, 10, ref lm, ref rm);
Check(lm == .8f && rm == .8f, "wide median placement unchanged");
lm = rm = .8f;
RoadSignPlacement.ConstrainToDivider(-3, 3, 3.2f, 7, ref lm, ref rm);
Check(math.abs(lm - .1f) < .001f, "painted divider midpoint");
Check(!RoadSignPlacement.RepeatOppositeSide(-1.75f,1.75f) && RoadSignPlacement.RepeatOppositeSide(-5,5),"ordinary roadside placement; wide approaches repeat for gameplay visibility");
foreach(var signOffset in new[]{-.25f,0f,.25f}) foreach(var scale in new[]{.5f,1f,2f})
{
    Check(SignAssemblyFrame.TryPlateAnchor(new float3(-.4f,0,signOffset-.01f),new float3(.4f,2.5f,signOffset+.01f),scale,1.3f,out var plate),"plate anchor uses native prefab bounds");
    Check(math.abs(plate.z+.012f-((signOffset+.01f)*scale+.02f))<.0001f,"supplementary FRONT aligned in front of primary metadata plane, including signed native mesh offsets");
    foreach(var direction in new[]{new float3(0,0,1),math.normalize(new float3(1,0,1)),new float3(-1,0,0)})
    {
        var rotation=quaternion.LookRotationSafe(-direction,math.up());
        Check(math.dot(math.rotate(rotation,new float3(0,0,1)),-direction)>.999f,"same positive frame faces vehicle approach on both roadsides");
    }
}
Check(!SignAssemblyFrame.TryPlateAnchor(default,new float3(float.NaN),1,1,out _),"ambiguous plate geometry skipped safely");
Console.WriteLine($"Road sign geometry: {checks} checks passed. Native rendering, theme assets and save/load require game testing.");
