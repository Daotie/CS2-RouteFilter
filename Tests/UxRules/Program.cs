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
var graph = new Dictionary<int,int[]> { [1]=new[] {2,3},[2]=new[] {1,4},[3]=new[] {1,4},[4]=new[] {2,3},[5]=Array.Empty<int>() };
Check(ConnectedRoadRange.Find(1,4,node => graph[node],100).SequenceEqual(new[] {1,2,4}),"deterministic fork chooses stable neighbour order");
Check(ConnectedRoadRange.Find(1,1,node => graph[node],100).SequenceEqual(new[] {1}),"same-edge range");
Check(ConnectedRoadRange.Find(1,5,node => graph[node],100).Count == 0,"disconnected endpoints cannot commit");
Check(ConnectedRoadRange.Find(1,4,node => graph[node],1).Count == 0,"bounded topology search");
// Actual production visual resolver: category coverage, never direction count.
var fleet=new Dictionary<int,TrafficVehicleSemantic> { [1]=TrafficVehicleSemantic.GoodsVehicle,[2]=TrafficVehicleSemantic.HeavyGoodsVehicle,[3]=TrafficVehicleSemantic.HeavyGoodsVehicle,[4]=TrafficVehicleSemantic.Bus,[5]=TrafficVehicleSemantic.RoadMaintenance,[6]=TrafficVehicleSemantic.RoadMaintenance,[7]=TrafficVehicleSemantic.FireEngine,[8]=TrafficVehicleSemantic.Ambulance,[9]=TrafficVehicleSemantic.PoliceVehicle };
TrafficLegend[] Mean(params int[] ids)=>TrafficSignSemantics.Resolve(ids,fleet.Keys,id=>fleet[id]);
var all=Mean(fleet.Keys.ToArray());
Check(TrafficSignSemantics.PrimaryFullyExpresses(all,TrafficVehicleSemantic.AllRoadMotorVehicles),"all motor vehicles: no redundant plate");
Check(!TrafficSignSemantics.PrimaryFullyExpresses(Mean(1),TrafficVehicleSemantic.AllRoadMotorVehicles),"all directions cannot imply all vehicles");
Check(Mean(1,2,3).Single().Semantic==TrafficVehicleSemantic.GoodsVehicle && !Mean(1,2,3)[0].Partial,"complete goods family consolidates light/heavy subclasses");
Check(Mean(2,3).Single().Semantic==TrafficVehicleSemantic.HeavyGoodsVehicle && !Mean(2,3)[0].Partial,"large goods restriction never widens to all goods vehicles");
Check(Mean(2).Single().Partial,"subset of heavy goods retains selected qualifier");
Check(Mean(2,2,3).Length==1,"multiple prefabs and duplicates make one semantic legend");
Check(Mean(5,6).Single().Semantic==TrafficVehicleSemantic.RoadMaintenance,"native maintenance class remains one category");
Check(Mean(7,8,9).Single().Semantic==TrafficVehicleSemantic.EmergencyVehicle,"only complete emergency family consolidates");
Check(Mean(7).Single().Semantic==TrafficVehicleSemantic.FireEngine,"fire-only never claims all emergency vehicles");
Check(Mean(1,4,5,7).Single().Semantic==TrafficVehicleSemantic.SpecifiedVehicles,"complex mixed selection becomes concise safe description");
Check(Mean(99).Single().Semantic==TrafficVehicleSemantic.SpecifiedVehicles,"missing assets survive as generic visual semantics");
Check(Mean().Length==0,"empty restriction does not fabricate a category");
Check(TrafficSignSemantics.PrimaryFullyExpresses(Mean(1,2,3),TrafficVehicleSemantic.GoodsVehicle),"verified dedicated primary consumes redundant goods legend");
Check(!TrafficSignSemantics.PrimaryFullyExpresses(Mean(2),TrafficVehicleSemantic.HeavyGoodsVehicle),"dedicated primary cannot erase a partial scope qualifier");
for(int mask=1;mask<(1<<fleet.Count);mask++)
{
    var selected=fleet.Keys.Where((id,i)=>(mask&(1<<i))!=0).ToArray(); var legends=Mean(selected);
    Check(legends.Length>0 && legends.Length<=2,"all selection combinations have bounded nonempty semantics");
    foreach(var legend in legends)
    {
        Check(!TrafficSignLocalization.Text(legend,"zh-CN").Contains("TrafficSign."),"no localization keys on physical plate");
        if(!legend.Partial && !legend.Except && legend.Semantic!=TrafficVehicleSemantic.SpecifiedVehicles && legend.Semantic!=TrafficVehicleSemantic.GoodsVehicle && legend.Semantic!=TrafficVehicleSemantic.EmergencyVehicle && legend.Semantic!=TrafficVehicleSemantic.AllRoadMotorVehicles)
            Check(fleet.Where(pair=>pair.Value==legend.Semantic).All(pair=>selected.Contains(pair.Key)),"unqualified category never broadens partial selection");
    }
}
foreach(var locale in new[]{"en-US","en-GB","zh-CN","zh-HK","ja-JP"}) Check(SignageProfiles.Resolve("AUTO","NA",locale)=="US","US road family dominates language");
Check(SignageProfiles.Resolve("AUTO","EU","zh-CN")=="GENERIC_EUROPE","EU physical sign ignores Chinese text locale");
Check(SignageProfiles.Resolve("AUTO","EU","en-GB")=="GENERIC_EUROPE","EU physical sign ignores British text locale");
foreach(var locale in new[]{"zh-HK","ja-JP","en-US","de-DE"}) Check(SignageProfiles.Resolve("AUTO","EU",locale)=="GENERIC_EUROPE","unfinished / generic refinement uses safe baseline");
foreach(var profile in SignageProfiles.Exposed.Where(value=>value!="AUTO")) foreach(var locale in new[]{"en-US","zh-CN"}) foreach(var theme in new[]{"NA","EU"}) Check(SignageProfiles.Resolve(profile,theme,locale)==profile,"manual profile survives theme and language changes");
Check(!SignageProfiles.IsExposed("HK") && !SignageProfiles.IsExposed("JP"),"unreviewed regional profiles hidden");
Check(SignageProfiles.Resolve("AUTO","Unknown","zh-CN")=="GENERIC_EUROPE","unknown theme is not guessed from locale");
Check(TrafficSignLocalization.Text(new TrafficLegend(TrafficVehicleSemantic.GoodsVehicle),"zh-CN")=="载货汽车","formal CN goods term");
Check(TrafficSignLocalization.Text(new TrafficLegend(TrafficVehicleSemantic.HeavyGoodsVehicle),"zh-CN")=="大型载货汽车","precise large goods term");
Check(TrafficSignLocalization.Text(new TrafficLegend(TrafficVehicleSemantic.RoadMaintenance),"en-US")=="ROAD MAINTENANCE","short professional maintenance legend");
Check(TrafficSignLocalization.Text(new TrafficLegend(TrafficVehicleSemantic.GoodsVehicle),"en-GB")=="GOODS VEHICLES","independent British terminology");
Check(TrafficSignLocalization.Text(new TrafficLegend(TrafficVehicleSemantic.GoodsVehicle),"en-US")=="TRUCKS","independent American terminology");
Check(TrafficSignLocalization.Language("zh-HK")=="en-US" && TrafficSignLocalization.Language("ja-JP")=="en-US","unreviewed locales use explicit English fallback");
var exceptGoods=Mean(4,5,6,7,8,9).Single();
Check(exceptGoods.Except && exceptGoods.Semantic==TrafficVehicleSemantic.GoodsVehicle,"whole light/heavy goods family exemption");
var exceptBus=Mean(fleet.Keys.Where(id=>id!=4).ToArray()).Single();
Check(exceptBus.Except && exceptBus.Semantic==TrafficVehicleSemantic.Bus,"complete bus exemption");
Check(TrafficSignLocalization.Text(exceptBus,"en-US")=="EXCEPT BUSES","GB exception syntax even on US signs");
Check(TrafficSignLocalization.Text(exceptBus,"zh-CN")=="除公共汽车外","localized exception meaning");
Check(!Mean(fleet.Keys.Where(id=>id!=2).ToArray()).Any(l=>l.Except),"partial category exemption must not imply whole family");
Check(!TrafficSignSemantics.PrimaryFullyExpresses(new[]{exceptBus},TrafficVehicleSemantic.Bus),"exception plate cannot disappear beneath prohibition");
foreach(var profile in new[]{"GENERIC_EUROPE","US"})
{
 Check(SignageProfiles.SupplementaryAllowed(profile,false,false),"reviewed supplementary profile enabled");
 Check(!SignageProfiles.SupplementaryAllowed(profile,true,false),"custom requires explicit force checkbox");
 Check(SignageProfiles.SupplementaryAllowed(profile,true,true),"force custom supplementary enabled");
 var bottom=SignAppearance.FaceBottom(2.5f,.8f,profile);var first=SignAppearance.FirstPlateHeight(bottom,2,.04f);var lift=Math.Max(0,first+.125f+.18f-bottom);
 Check(first-lift+.125f<=bottom-.18f+.0001f,"supplementary face below lowest sign face including US Wrong Way reserve");
}
Check(!SignageProfiles.IsExposed("CN")&&!SignageProfiles.IsExposed("UK"),"unfinished profiles removed");
var named=TrafficSignSemantics.Resolve(new[]{10},new[]{10,11},id=>TrafficVehicleSemantic.SpecifiedVehicles,id=>"City bicycle").Single();
Check(TrafficSignLocalization.Text(named,"en-GB")=="City bicycle","unclassified selection identifies actual asset instead of bare selected vehicles");
var goodsLegend=new TrafficLegend(TrafficVehicleSemantic.GoodsVehicle);
Check(TrafficSignLocalization.ResolveText(goodsLegend,"en-GB",key=>"GOODS VEHICLES")=="GOODS VEHICLES","exact reviewed dictionary overrides semantic text");
Check(TrafficSignLocalization.ResolveText(goodsLegend,"zh-HANS",key=>"载货汽车")=="载货汽车","supported base language dictionary");
Check(TrafficSignLocalization.ResolveText(goodsLegend,"zh-HK",key=>"载货汽车")=="TRUCKS","unreviewed locale never inherits mainland terminology");
Check(TrafficSignLocalization.ResolveText(goodsLegend,"zh-CN",key=>key)=="载货汽车","missing key never rendered");
Check(TrafficSignLocalization.ResolveText(goodsLegend,"zh-CN",key=>new string('x',65))=="载货汽车","invalid overlong legend uses equivalent semantic fallback");
var history=new[]{"Old truck","Missing mod prefab","Bus"};
Check(RecentAssetHistory.AfterSuccessfulApply(history,new[]{"Bus","Truck","Bus"}).SequenceEqual(new[]{"Bus","Truck","Old truck","Missing mod prefab"}),"Recent successful batch first, stable ties and deduplication");
Check(RecentAssetHistory.AfterSuccessfulApply(history,Array.Empty<string>()).SequenceEqual(history),"allow-all Apply does not invent Recent assets");
Check(RecentAssetHistory.AfterSuccessfulApply(Enumerable.Range(0,70).Select(i=>"Asset"+i),new[]{"New"}).Length==64,"Recent history bounded");
Check(RecentAssetHistory.AfterSuccessfulApply(history,new[]{"中文|Stable%Id"})[0]=="中文|Stable%Id","Recent stores stable identities, independent of catalog numbers and locale");
// Reproduce the native collection failure without requiring a running ECS world.
var nativeLike = new IndexedOnlyAssets(new[] { "Truck", "Bus", "Truck" });
bool genericFailed = false;
try { nativeLike.Distinct().ToArray(); } catch (NotImplementedException) { genericFailed = true; }
Check(genericFailed, "regression fixture rejects generic enumeration like game DynamicBuffer");
var snapshot = AssetLibrarySnapshot.Read(nativeLike.Count, i => nativeLike[i]);
Check(snapshot.Distinct().SequenceEqual(new[] { "Truck", "Bus" }), "indexed snapshot supports Copy deduplication without native enumeration");
Check(RecentAssetHistory.AfterSuccessfulApply(history, snapshot).Take(2).SequenceEqual(new[] { "Bus", "Truck" }), "indexed committed buffer populates Recent");
var library = new[] { (Id: 11, Name: "Truck", Mode: 1), (Id: 22, Name: "Bus", Mode: 1), (Id: 33, Name: "Train", Mode: 2) };
// Actual paste resolver receives no source/target entity: all four road target combinations share it.
foreach (var source in new[] { "Node", "Segment" })
foreach (var destination in new[] { "Node", "Segment" })
    Check(AssetLibrarySnapshot.Compatible(library, snapshot, 1, a => a.Name, a => a.Mode).Select(a => a.Id).SequenceEqual(new[] { 11, 22 }), $"{source} to {destination}: stable identities survive different target/catalog IDs");
Check(AssetLibrarySnapshot.Compatible(library, snapshot, 2, a => a.Name, a => a.Mode).Length == 0, "incompatible road clipboard does not replace rail pending selection");
Check(AssetLibrarySnapshot.Compatible(library, new[] { "Missing", "Bus" }, 1, a => a.Name, a => a.Mode).Single().Id == 22, "missing prefab does not discard compatible clipboard assets");
Console.WriteLine($"UX / traffic semantics / profiles: {checks} checks passed.");

sealed class IndexedOnlyAssets : System.Collections.Generic.IEnumerable<string>
{
    private readonly string[] values;
    public IndexedOnlyAssets(string[] values) { this.values = values; }
    public int Count => values.Length;
    public string this[int index] => values[index];
    public System.Collections.Generic.IEnumerator<string> GetEnumerator() => throw new NotImplementedException();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
}
