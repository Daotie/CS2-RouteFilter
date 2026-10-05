using System;
using System.Collections.Generic;
using System.Linq;
namespace RouteFilter.Persistence;
// Visual-only classification. Never used by matching, topology, admission or persistence.
internal enum TrafficVehicleSemantic { SpecifiedVehicles, AllRoadMotorVehicles, GoodsVehicle, HeavyGoodsVehicle, Bus, Taxi, Motorcycle, WorkVehicle, RoadMaintenance, EmergencyVehicle, FireEngine, Ambulance, PoliceVehicle, RefuseVehicle, PassengerCar, MunicipalVehicle }
internal enum SupplementaryLegendKind { Restricted, Except, Notice }
internal readonly struct TrafficLegend
{
    internal readonly TrafficVehicleSemantic Semantic;
    internal readonly bool Partial;
    internal readonly bool Except;
    internal readonly TrafficVehicleSemantic[] Subjects;
    internal readonly SupplementaryLegendKind Kind;
    internal TrafficLegend(TrafficVehicleSemantic semantic,bool partial=false,bool except=false,TrafficVehicleSemantic[] subjects=null)
    { Semantic=semantic; Partial=partial; Except=except; Subjects=subjects??new[]{semantic}; Kind=semantic==TrafficVehicleSemantic.SpecifiedVehicles?SupplementaryLegendKind.Notice:except?SupplementaryLegendKind.Except:SupplementaryLegendKind.Restricted; }
    internal string Key => "TrafficSign.VehicleCategory."+Kind+"."+string.Join(".",Subjects)+(Partial?".Selected":"");
}
internal static class TrafficSignSemantics
{
    internal static TrafficLegend[] Resolve<T>(IEnumerable<T> selected,IEnumerable<T> applicable,Func<T,TrafficVehicleSemantic> classify,Func<T,string> describe=null)
    {
        var chosen=new HashSet<T>(selected); var available=new HashSet<T>(applicable);
        if(chosen.Count==0)return Array.Empty<TrafficLegend>();
        if(available.Count>0 && chosen.SetEquals(available))return new[]{new TrafficLegend(TrafficVehicleSemantic.AllRoadMotorVehicles)};
        if(!chosen.IsSubsetOf(available))return Notice();
        Func<T,TrafficVehicleSemantic> signCategory=id=>classify(id)==TrafficVehicleSemantic.RoadMaintenance || classify(id)==TrafficVehicleSemantic.RefuseVehicle?TrafficVehicleSemantic.MunicipalVehicle:classify(id);
        var restricted=Candidate(chosen,available,signCategory,false);
        var allowed=new HashSet<T>(available.Where(id=>!chosen.Contains(id)));
        var exception=Candidate(allowed,available,signCategory,true);
        // Compare equivalent descriptions only; an exception must cover complete classes.
        if(exception.HasValue && (!restricted.HasValue || Cost(exception.Value)<Cost(restricted.Value)))return new[]{exception.Value};
        return restricted.HasValue?new[]{restricted.Value}:Notice();
    }
    private static TrafficLegend[] Notice()=>new[]{new TrafficLegend(TrafficVehicleSemantic.SpecifiedVehicles)};
    private static int Cost(TrafficLegend legend)=>TrafficSignLocalization.Text(legend,"zh-CN").Replace("\n","").Length;
    private static TrafficLegend? Candidate<T>(HashSet<T> scope,HashSet<T> available,Func<T,TrafficVehicleSemantic> classify,bool except)
    {
        if(scope.Count==0 || scope.Any(id=>classify(id)==TrafficVehicleSemantic.SpecifiedVehicles))return null;
        var groups=scope.Select(classify).Distinct().ToList();
        foreach(var family in new[]{TrafficVehicleSemantic.GoodsVehicle,TrafficVehicleSemantic.EmergencyVehicle,TrafficVehicleSemantic.MunicipalVehicle})
        {
            var members=available.Where(id=>Belongs(classify(id),family)).ToArray();
            if(members.Length>0 && members.All(scope.Contains) && groups.Count(value=>Belongs(value,family))>1)
            { groups.RemoveAll(value=>Belongs(value,family)); groups.Add(family); }
        }
        groups.Sort();
        if(groups.Count>3)return null;
        var partial=groups.Any(group=>available.Any(id=>Belongs(classify(id),group)&&!scope.Contains(id)));
        // A partial exception would accidentally allow other assets in the same class.
        if(partial && (except || groups.Count>1))return null;
        var legend=new TrafficLegend(groups[0],partial,except,subjects:groups.ToArray());
        // Longer descriptions use a neutral notice, never a made-up umbrella class.
        return Cost(legend)<=22?legend:(TrafficLegend?)null;
    }
    internal static bool Belongs(TrafficVehicleSemantic value,TrafficVehicleSemantic group)=>value==group || group==TrafficVehicleSemantic.GoodsVehicle && (value==TrafficVehicleSemantic.GoodsVehicle || value==TrafficVehicleSemantic.HeavyGoodsVehicle) || group==TrafficVehicleSemantic.EmergencyVehicle && (value==TrafficVehicleSemantic.EmergencyVehicle || value==TrafficVehicleSemantic.FireEngine || value==TrafficVehicleSemantic.Ambulance || value==TrafficVehicleSemantic.PoliceVehicle) || group==TrafficVehicleSemantic.MunicipalVehicle && (value==TrafficVehicleSemantic.RoadMaintenance || value==TrafficVehicleSemantic.RefuseVehicle);
    internal static bool PrimaryFullyExpresses(TrafficLegend[] meanings,TrafficVehicleSemantic? primary,bool custom=false)=>
        !custom && meanings.Length==1 && meanings[0].Subjects.Length==1 && !meanings[0].Partial && meanings[0].Kind==SupplementaryLegendKind.Restricted && primary==meanings[0].Semantic;
}
internal static class SignageProfiles
{
    internal static readonly string[] Exposed={"AUTO","GENERIC_EUROPE","US"};
    internal static bool SupplementaryAllowed(string profile,bool custom,bool force)=>(profile=="US" || profile=="GENERIC_EUROPE") && (!custom || force);
    internal static bool IsExposed(string value)=>Exposed.Contains(value,StringComparer.Ordinal);
    internal static string Resolve(string choice,string themePrefix,string locale)
    {
        if (IsExposed(choice) && choice!="AUTO") return choice;
        if (themePrefix=="NA") return "US"; // ThemePrefab.assetPrefix metadata, never name substrings.
        // Physical primary follows the road theme. Supplementary text resolves
        // independently from the active locale; only EU/US profiles are currently exposed.
        return "GENERIC_EUROPE";
    }
}
internal static class TrafficSignLocalization
{
    // Independent semantic dictionaries: no UI labels, prefab names or internal keys on plates.
    private static readonly string[] EnglishUS={"SELECTED VEHICLES","ALL MOTOR VEHICLES","TRUCKS","LARGE TRUCKS","BUSES","TAXIS","MOTORCYCLES","WORK VEHICLES","ROAD MAINTENANCE","EMERGENCY VEHICLES","FIRE ENGINES","AMBULANCES","POLICE VEHICLES","REFUSE VEHICLES","PASSENGER CARS","MUNICIPAL VEHICLES"};
    private static readonly string[] EnglishGB={"SELECTED VEHICLES","ALL MOTOR VEHICLES","GOODS VEHICLES","LARGE GOODS VEHICLES","BUSES","TAXIS","MOTORCYCLES","WORK VEHICLES","ROAD MAINTENANCE","EMERGENCY VEHICLES","FIRE ENGINES","AMBULANCES","POLICE VEHICLES","REFUSE VEHICLES","CARS","MUNICIPAL VEHICLES"};
    private static readonly string[] ChineseCN={"车辆限行","全部机动车","货车","大型货车","公交车","出租车","摩托车","工程车辆","市政车辆","紧急车辆","消防车","救护车","警车","环卫车","小客车","市政车辆"};
    internal static string Language(string locale)
    {
        var id=(locale??"").Replace('_','-').ToLowerInvariant();
        if (id=="en-gb") return "en-GB";
        if (id=="zh-cn" || id=="zh-hans" || id.StartsWith("zh-hans-")) return "zh-CN";
        // Traditional Chinese / Japanese are not silently converted into Mainland Chinese.
        return "en-US";
    }
    internal static string ResolveText(TrafficLegend legend,string locale,Func<string,string> translated)=>Text(legend,locale);
    internal static string Text(TrafficLegend legend,string locale)
    {
        var language=Language(locale);
        if(legend.Kind==SupplementaryLegendKind.Notice)return language=="zh-CN"?"车辆限行":"VEHICLE RESTRICTIONS";
        var dictionary=language=="zh-CN"?ChineseCN:language=="en-GB"?EnglishGB:EnglishUS;
        var names=legend.Subjects.Select(subject=>dictionary[(int)subject]).ToArray();
        if(language=="zh-CN")
        {
            var suffix=legend.Except?"除外":"";
            var prefix=legend.Partial?"部分":"";
            var line=prefix+string.Join("、",names)+suffix;
            if(line.Length<=10)return line;
            // Break only between complete category names; the exception suffix stays intact.
            if(names.Length>1)
            {
                var best=1;
                for(int i=1;i<names.Length;i++)
                    if(Math.Abs(string.Join("、",names.Take(i)).Length-(string.Join("、",names.Skip(i)).Length+suffix.Length))<Math.Abs(string.Join("、",names.Take(best)).Length-(string.Join("、",names.Skip(best)).Length+suffix.Length)))best=i;
                return string.Join("、",names.Take(best))+"\n"+string.Join("、",names.Skip(best))+suffix;
            }
            return line;
        }
        return (legend.Except?"EXCEPT ":legend.Partial?"SELECTED ":"")+string.Join(", ",names);
    }
}
