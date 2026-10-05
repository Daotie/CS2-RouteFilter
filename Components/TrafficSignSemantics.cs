using System;
using System.Collections.Generic;
using System.Linq;
namespace RouteFilter.Persistence;
// Visual-only classification. Never used by matching, topology, admission or persistence.
internal enum TrafficVehicleSemantic { SpecifiedVehicles, AllRoadMotorVehicles, GoodsVehicle, HeavyGoodsVehicle, Bus, Taxi, Motorcycle, WorkVehicle, RoadMaintenance, EmergencyVehicle, FireEngine, Ambulance, PoliceVehicle, RefuseVehicle, PassengerCar }
internal readonly struct TrafficLegend
{
    internal readonly TrafficVehicleSemantic Semantic;
    internal readonly bool Partial;
    internal TrafficLegend(TrafficVehicleSemantic semantic,bool partial=false) { Semantic=semantic; Partial=partial; }
    internal string Key => "TrafficSign.VehicleCategory."+Semantic+(Partial ? ".Selected" : "");
}
internal static class TrafficSignSemantics
{
    internal static TrafficLegend[] Resolve<T>(IEnumerable<T> selected,IEnumerable<T> applicable,Func<T,TrafficVehicleSemantic> classify)
    {
        var chosen=new HashSet<T>(selected); var available=new HashSet<T>(applicable);
        if (chosen.Count==0) return Array.Empty<TrafficLegend>();
        // A missing/unclassified selected asset must not disappear or imply broader coverage.
        if (available.Count>0 && chosen.SetEquals(available)) return new[] {new TrafficLegend(TrafficVehicleSemantic.AllRoadMotorVehicles)};
        if (chosen.Any(id=>!available.Contains(id) || classify(id)==TrafficVehicleSemantic.SpecifiedVehicles)) return Generic();
        var groups=chosen.GroupBy(classify).ToDictionary(g=>g.Key,g=>new HashSet<T>(g));
        // All goods subclasses collapse only when the entire goods family is selected.
        var goods=available.Where(id=>IsGoods(classify(id))).ToArray();
        if (goods.Length>0 && goods.All(chosen.Contains) && groups.Keys.Any(IsGoods))
        { groups.Remove(TrafficVehicleSemantic.GoodsVehicle); groups.Remove(TrafficVehicleSemantic.HeavyGoodsVehicle); groups[TrafficVehicleSemantic.GoodsVehicle]=new HashSet<T>(goods); }
        var emergency=available.Where(id=>IsEmergency(classify(id))).ToArray();
        if (emergency.Length>0 && emergency.All(chosen.Contains) && groups.Keys.Any(IsEmergency))
        { foreach (var key in groups.Keys.Where(IsEmergency).ToArray()) groups.Remove(key); groups[TrafficVehicleSemantic.EmergencyVehicle]=new HashSet<T>(emergency); }
        // Consolidate BEFORE rendering, rather than clipping a category tower.
        if (groups.Count>2) return Generic();
        return groups.OrderBy(g=>g.Key).Select(g=>new TrafficLegend(g.Key,
            available.Any(id=>Belongs(classify(id),g.Key) && !chosen.Contains(id)))).ToArray();
    }
    private static TrafficLegend[] Generic()=>new[] {new TrafficLegend(TrafficVehicleSemantic.SpecifiedVehicles)};
    private static bool IsGoods(TrafficVehicleSemantic value)=>value==TrafficVehicleSemantic.GoodsVehicle || value==TrafficVehicleSemantic.HeavyGoodsVehicle;
    private static bool IsEmergency(TrafficVehicleSemantic value)=>value==TrafficVehicleSemantic.EmergencyVehicle || value==TrafficVehicleSemantic.FireEngine || value==TrafficVehicleSemantic.Ambulance || value==TrafficVehicleSemantic.PoliceVehicle;
    private static bool Belongs(TrafficVehicleSemantic value,TrafficVehicleSemantic group)=>value==group || group==TrafficVehicleSemantic.GoodsVehicle && IsGoods(value) || group==TrafficVehicleSemantic.EmergencyVehicle && IsEmergency(value);
    // NoEntry can express the complete set; category-specific primary capabilities are optional.
    internal static bool PrimaryFullyExpresses(TrafficLegend[] meanings,TrafficVehicleSemantic? primary,bool custom=false)=>
        !custom && meanings.Length==1 && !meanings[0].Partial && primary==meanings[0].Semantic;
}
internal static class SignageProfiles
{
    internal static readonly string[] Exposed={"AUTO","GENERIC_EUROPE","CN","UK","US"};
    internal static bool IsExposed(string value)=>Exposed.Contains(value,StringComparer.Ordinal);
    internal static string Resolve(string choice,string themePrefix,string locale)
    {
        if (IsExposed(choice) && choice!="AUTO") return choice;
        if (themePrefix=="NA") return "US"; // ThemePrefab.assetPrefix metadata, never name substrings.
        if (themePrefix!="EU") return "GENERIC_EUROPE";
        var language=(locale??"").Replace('_','-').ToLowerInvariant();
        if (language=="zh-cn" || language=="zh-hans" || language.StartsWith("zh-hans-")) return "CN";
        if (language=="en-gb") return "UK";
        // HK and JP remain architectural IDs only, until independently completed.
        return "GENERIC_EUROPE";
    }
}
internal static class TrafficSignLocalization
{
    // Independent semantic dictionaries: no UI labels, prefab names or internal keys on plates.
    private static readonly string[] EnglishUS={"SELECTED VEHICLES","ALL MOTOR VEHICLES","TRUCKS","LARGE TRUCKS","BUSES","TAXIS","MOTORCYCLES","WORK VEHICLES","ROAD MAINTENANCE","EMERGENCY VEHICLES","FIRE ENGINES","AMBULANCES","POLICE VEHICLES","REFUSE VEHICLES","PASSENGER CARS"};
    private static readonly string[] EnglishGB={"SELECTED VEHICLES","ALL MOTOR VEHICLES","GOODS VEHICLES","LARGE GOODS VEHICLES","BUSES","TAXIS","MOTORCYCLES","WORK VEHICLES","ROAD MAINTENANCE","EMERGENCY VEHICLES","FIRE ENGINES","AMBULANCES","POLICE VEHICLES","REFUSE VEHICLES","CARS"};
    private static readonly string[] ChineseCN={"指定车辆","全部机动车","载货汽车","大型载货汽车","公共汽车","出租汽车","摩托车","工程车辆","道路养护车辆","紧急车辆","消防车辆","救护车辆","警用车辆","环卫车辆","乘用汽车"};
    internal static string Language(string locale)
    {
        var id=(locale??"").Replace('_','-').ToLowerInvariant();
        if (id=="en-gb") return "en-GB";
        if (id=="zh-cn" || id=="zh-hans" || id.StartsWith("zh-hans-")) return "zh-CN";
        // Traditional Chinese / Japanese are not silently converted into Mainland Chinese.
        return "en-US";
    }
    internal static string ResolveText(TrafficLegend legend,string locale,Func<string,string> translated)
    {
        var language=Language(locale);
        var normalized=(locale??"").Replace('_','-');
        if(string.Equals(language,normalized,StringComparison.OrdinalIgnoreCase) || language=="zh-CN" && normalized.StartsWith("zh-HANS",StringComparison.OrdinalIgnoreCase))
        {
            var candidate=translated(legend.Key);
            if(!string.IsNullOrWhiteSpace(candidate) && candidate!=legend.Key && candidate.Length<=64) return candidate;
        }
        return Text(legend,locale);
    }
    internal static string Text(TrafficLegend legend,string locale)
    {
        var language=Language(locale); var i=(int)legend.Semantic;
        var dictionary=language=="zh-CN"?ChineseCN:language=="en-GB"?EnglishGB:EnglishUS;
        var text=i>=0 && i<dictionary.Length?dictionary[i]:dictionary[0];
        return legend.Partial?(language=="zh-CN"?"指定"+text:"SELECTED "+text):text;
    }
}
