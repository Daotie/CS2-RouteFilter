using RouteFilter.Persistence;
using RouteFilter.Systems;

int checks = 0;
void Check(bool valid,string reason) { checks++; if (!valid) throw new Exception(reason); }
var presets = new[] { new UserPreset { Name = "中文 | test\n", Assets = new[] { "Truck|01", "Van\nTwo", "资产%3" } }, new UserPreset { Name = "Empty", Assets = Array.Empty<string>() } };
var decoded = UserPreferenceData.Read(UserPreferenceData.Write(presets));
Check(decoded.Count == 2 && decoded[0].Name == presets[0].Name.Trim() && decoded[0].Assets.SequenceEqual(presets[0].Assets),"unicode and delimiter roundtrip");
Check(decoded[1].Assets.Length == 0,"allow-all preset survives roundtrip");
Check(UserPreferenceData.Read("2\nfuture").Count == 0,"unknown preference version rejected");
Check(UserPreferenceData.Read(new string('x',2097153)).Count == 0,"bounded preference input");
Check(UserPreferenceData.Read("1\nA|x|x\nA|y\nB|z").Count == 2,"duplicate preset names rejected");
Check(UserPreferenceData.Read("1\nA|x|x")[0].Assets.Length == 1,"duplicate assets deduplicated");
Check(UserPreferenceData.Read("1\n" + new string('x',81) + "|z").Count == 0,"overlong preset names rejected");
Check(UserPreferenceData.Read("1\nA|" + new string('x',513))[0].Assets.Length == 0,"overlong asset names excluded");
Check(UserPreferenceData.Read(UserPreferenceData.Write(Enumerable.Range(0,70).Select(i => new UserPreset { Name = "P"+i,Assets=Array.Empty<string>() }))).Count == 64,"preset count bound");
foreach (var spacing in new[] { .02f,.04f,.2f })
foreach (var bottom in new[] { .7f,1.5f,3.4f })
for (int count = 1; count <= 32; count++)
{
    float first = SignAppearance.FirstPlateHeight(bottom,count,spacing);
    float lowest = SignAppearance.PlateHeight(first,count-1,spacing)-.125f;
    Check(lowest >= .3499f,"many-category stack retains ground clearance");
    Check(first + .125f + .18f >= bottom-.0001f,"lift covers the required main-to-plate gap");
    if (count > 1) Check(SignAppearance.PlateHeight(first,1,spacing) < first,"stack extends downwards");
}
Check(SignAppearance.Clamp(float.NaN,0,5,1) == 1 && SignAppearance.Clamp(float.PositiveInfinity,0,5,1) == 1,"nonfinite appearance sanitized");
Check(SignAppearance.Clamp(-100,.5f,2,1) == .5f && SignAppearance.Clamp(100,.5f,2,1) == 2,"appearance clamps bounds");
var stroke = new PendingBrushBatch<int>(); var assets = new List<int>{5,6};
Check(!stroke.Add(1),"inactive brush cannot queue targets");
stroke.Begin(false,assets); assets.Add(7);
Check(stroke.Assets.SequenceEqual(new[] {5,6}),"assets frozen at stroke start");
Check(stroke.Add(42) && !stroke.Add(42) && stroke.Targets.Count == 1,"stroke deduplicates targets");
stroke.Reset(); Check(!stroke.Active && stroke.Targets.Count == 0 && stroke.Assets.Count == 0,"cancel/disable/reset discards pending work");
stroke.Begin(true,assets); Check(stroke.Clear,"RMB clear stroke retains action");
for (int i = 0; i < 5000; i++) stroke.Add(i);
Check(stroke.Targets.Count == 4096,"bounded brush stroke");
stroke.Begin(false,Array.Empty<int>()); Check(!stroke.Clear && stroke.Targets.Count == 0,"new stroke cannot reuse old pending targets");
Console.WriteLine($"UX preference / appearance / brush rules: {checks} checks passed.");
