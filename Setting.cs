using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Pathfind;
using Game.Settings;
using NodeGate.Components;
using System.Collections.Generic;

namespace NodeGate;

[FileLocation(Mod.Id)]
[SettingsUIGroupOrder(kBehaviorGroup, kToolGroup)]
[SettingsUIShowGroupName(kBehaviorGroup, kToolGroup)]
[SettingsUIKeyboardAction(Mod.ToggleToolAction, ActionType.Button, usages: new[] { Usages.kMenuUsage })]
[SettingsUIMouseAction(Mod.ApplyAction, ActionType.Button, usages: new[] { Usages.kMenuUsage })]
[SettingsUIMouseAction(Mod.ClearAction, ActionType.Button, usages: new[] { Usages.kMenuUsage })]
public sealed class Setting : ModSetting
{
    public const string kSection = "Main";
    public const string kBehaviorGroup = "Behavior";
    public const string kToolGroup = "Tool";

    public Setting(IMod mod) : base(mod) => SetDefaults();

    [SettingsUISection(kSection, kBehaviorGroup)] public bool EnableExactGate { get; set; }
    [SettingsUISection(kSection, kBehaviorGroup)] public bool EnableNativeRerouting { get; set; }
    [SettingsUISection(kSection, kBehaviorGroup)] public bool ProtectEmergencyVehicles { get; set; }

    [SettingsUISection(kSection, kBehaviorGroup)]
    [SettingsUISlider(min = 1, max = 8, step = 1, scalarMultiplier = 1, unit = Game.UI.Unit.kInteger)]
    public int LookAheadLanes { get; set; }

    [SettingsUIKeyboardBinding(BindingKeyboard.X, Mod.ToggleToolAction, ctrl: true, shift: true)]
    [SettingsUISection(kSection, kToolGroup)]
    public ProxyBinding ToggleToolBinding { get; set; }

    [SettingsUIMouseBinding(BindingMouse.Left, Mod.ApplyAction)] [SettingsUIHidden]
    public ProxyBinding ApplyBinding { get; set; }

    [SettingsUIMouseBinding(BindingMouse.Right, Mod.ClearAction)] [SettingsUIHidden]
    public ProxyBinding ClearBinding { get; set; }

    public RuleFlags GetNativeRules(VehicleTypeMask mask)
    {
        if (!EnableNativeRerouting) return default;
        var result = default(RuleFlags);
        if ((mask & VehicleTypeMask.PrivateCar) != 0) result |= RuleFlags.ForbidPrivateTraffic;
        const VehicleTypeMask publicModes = VehicleTypeMask.Bus | VehicleTypeMask.Tram | VehicleTypeMask.PassengerTrain | VehicleTypeMask.Subway;
        if ((mask & publicModes) == publicModes) result |= RuleFlags.ForbidTransitTraffic;
        const VehicleTypeMask cargoModes = VehicleTypeMask.DeliveryTruck | VehicleTypeMask.GoodsDelivery | VehicleTypeMask.CargoTrain;
        if ((mask & cargoModes) == cargoModes) result |= RuleFlags.ForbidHeavyTraffic;
        return result;
    }

    public override void SetDefaults()
    {
        EnableExactGate = true;
        EnableNativeRerouting = false;
        ProtectEmergencyVehicles = false;
        LookAheadLanes = 3;
    }
}

internal abstract class LocaleBase : IDictionarySource
{
    protected readonly Setting Setting;
    protected LocaleBase(Setting setting) => Setting = setting;
    protected abstract bool Chinese { get; }

    public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
    {
        var entries = new Dictionary<string, string>
        {
            [Setting.GetSettingsLocaleID()] = Chinese ? "NodeGate 节点通行" : "NodeGate",
            [Setting.GetOptionTabLocaleID(Setting.kSection)] = Chinese ? "主要设置" : "General",
            [Setting.GetOptionGroupLocaleID(Setting.kBehaviorGroup)] = Chinese ? "拦截行为" : "Gate behavior",
            [Setting.GetOptionGroupLocaleID(Setting.kToolGroup)] = Chinese ? "工具" : "Tool",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableExactGate))] = Chinese ? "启用精确车型闸门" : "Enable exact vehicle gate",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableExactGate))] = Chinese ? "在节点入口按具体车型强制拦截。" : "Hard-stops selected vehicle types at restricted nodes.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableNativeRerouting))] = Chinese ? "启用原生大类绕行" : "Enable native broad rerouting",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableNativeRerouting))] = Chinese ? "对完整选择的私家、货运或公共交通大类追加游戏原生寻路规则。" : "Adds vanilla pathfinding rules for complete private, cargo, or transit groups.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ProtectEmergencyVehicles))] = Chinese ? "始终放行应急车辆" : "Always allow emergency vehicles",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ProtectEmergencyVehicles))] = Chinese ? "不拦截警车、救护车和消防车。" : "Never blocks police cars, ambulances, or fire engines.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.LookAheadLanes))] = Chinese ? "前瞻车道数" : "Look-ahead lanes",
            [Setting.GetOptionDescLocaleID(nameof(Setting.LookAheadLanes))] = Chinese ? "检测前方多少段导航车道。" : "Number of upcoming navigation lanes checked before the gate.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "开关 NodeGate 工具" : "Toggle NodeGate tool",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "也可以点击游戏左上角按钮。" : "You can also use the top-left game UI button.",
            [Setting.GetBindingMapLocaleID()] = "NodeGate",
            [Setting.GetBindingKeyLocaleID(Mod.ToggleToolAction)] = Chinese ? "开关 NodeGate 工具" : "Toggle NodeGate tool",
            [Setting.GetBindingKeyLocaleID(Mod.ApplyAction)] = Chinese ? "应用节点限制" : "Apply node restrictions",
            [Setting.GetBindingKeyLocaleID(Mod.ClearAction)] = Chinese ? "清除节点限制" : "Clear node restrictions",
            ["NodeGate.UI.Title"] = Chinese ? "节点通行" : "NodeGate",
            ["NodeGate.UI.Instruction"] = Chinese ? "选择车型后，左键节点应用，右键清除。" : "Choose types, then left-click a node to apply or right-click to clear.",
            ["NodeGate.UI.Active"] = Chinese ? "工具已启用" : "Tool active",
            ["NodeGate.UI.Inactive"] = Chinese ? "打开工具" : "Open tool",
        };
        foreach (var pair in VehicleLabels(Chinese)) entries[$"NodeGate.Vehicle.{pair.Key}"] = pair.Value;
        return entries;
    }

    private static Dictionary<string, string> VehicleLabels(bool zh) => new()
    {
        ["PrivateCar"] = zh ? "私家车" : "Private cars", ["Taxi"] = zh ? "出租车" : "Taxis",
        ["DeliveryTruck"] = zh ? "配送货车" : "Delivery trucks", ["GoodsDelivery"] = zh ? "商品配送车" : "Goods delivery",
        ["Bus"] = zh ? "公交车" : "Buses", ["Tram"] = zh ? "有轨电车" : "Trams",
        ["PassengerTrain"] = zh ? "客运列车" : "Passenger trains", ["Subway"] = zh ? "地铁列车" : "Subway trains",
        ["CargoTrain"] = zh ? "货运列车" : "Cargo trains", ["PoliceCar"] = zh ? "警车" : "Police cars",
        ["Ambulance"] = zh ? "救护车" : "Ambulances", ["FireEngine"] = zh ? "消防车" : "Fire engines",
        ["GarbageTruck"] = zh ? "垃圾车" : "Garbage trucks", ["Hearse"] = zh ? "灵车" : "Hearses",
        ["RoadMaintenance"] = zh ? "道路养护车" : "Road maintenance", ["ParkMaintenance"] = zh ? "公园养护车" : "Park maintenance",
        ["PostVan"] = zh ? "邮政车" : "Post vans", ["PrisonerTransport"] = zh ? "囚犯运输车" : "Prisoner transport",
        ["EvacuationTransport"] = zh ? "疏散车辆" : "Evacuation vehicles", ["Bicycle"] = zh ? "自行车" : "Bicycles",
    };

    public void Unload() { }
}

internal sealed class LocaleEN : LocaleBase { public LocaleEN(Setting setting) : base(setting) { } protected override bool Chinese => false; }
internal sealed class LocaleZH : LocaleBase { public LocaleZH(Setting setting) : base(setting) { } protected override bool Chinese => true; }
