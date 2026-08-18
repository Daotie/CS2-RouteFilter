using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace RouteFilter;

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

    [SettingsUISection(kSection, kBehaviorGroup)] public bool EnableExactRestrictions { get; set; }
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

    public override void SetDefaults()
    {
        EnableExactRestrictions = true;
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
        return new Dictionary<string, string>
        {
            [Setting.GetSettingsLocaleID()] = Chinese ? "RouteFilter 路线通行筛选" : "RouteFilter",
            [Setting.GetOptionTabLocaleID(Setting.kSection)] = Chinese ? "主要设置" : "General",
            [Setting.GetOptionGroupLocaleID(Setting.kBehaviorGroup)] = Chinese ? "通行限制行为" : "Restriction behavior",
            [Setting.GetOptionGroupLocaleID(Setting.kToolGroup)] = Chinese ? "工具" : "Tool",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableExactRestrictions))] = Chinese ? "启用逐资产限制" : "Enable asset-level restrictions",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableExactRestrictions))] = Chinese ? "按单个车辆资产限制节点或路段，并请求替代路线。" : "Restricts individual vehicle assets at nodes or segments and requests alternate routes.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ProtectEmergencyVehicles))] = Chinese ? "始终放行应急车辆" : "Always allow emergency vehicles",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ProtectEmergencyVehicles))] = Chinese ? "不拦截警车、救护车和消防车，即使选择了对应资产。" : "Never blocks police cars, ambulances, or fire engines, even when their assets are selected.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.LookAheadLanes))] = Chinese ? "前瞻车道数" : "Look-ahead lanes",
            [Setting.GetOptionDescLocaleID(nameof(Setting.LookAheadLanes))] = Chinese ? "检测前方多少段导航车道。" : "Number of upcoming navigation lanes checked for restrictions.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "开关 RouteFilter 工具" : "Toggle RouteFilter tool",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "也可以点击游戏左上角按钮。" : "You can also use the top-left game UI button.",
            [Setting.GetBindingMapLocaleID()] = "RouteFilter",
            [Setting.GetBindingKeyLocaleID(Mod.ToggleToolAction)] = Chinese ? "开关 RouteFilter 工具" : "Toggle RouteFilter tool",
            [Setting.GetBindingKeyLocaleID(Mod.ApplyAction)] = Chinese ? "应用限制" : "Apply restrictions",
            [Setting.GetBindingKeyLocaleID(Mod.ClearAction)] = Chinese ? "清除限制" : "Clear restrictions",
            ["RouteFilter.UI.Title"] = Chinese ? "路线通行筛选" : "RouteFilter",
            ["RouteFilter.UI.Instruction"] = Chinese ? "选择目标与车辆资产；左键应用限制，右键清除。" : "Choose a target and vehicle assets; left-click to apply or right-click to clear.",
            ["RouteFilter.UI.Active"] = Chinese ? "工具已启用" : "Tool active",
            ["RouteFilter.UI.Inactive"] = Chinese ? "打开工具" : "Open tool",
            ["RouteFilter.UI.Node"] = Chinese ? "节点" : "Node",
            ["RouteFilter.UI.Segment"] = Chinese ? "整段路段" : "Segment",
            ["RouteFilter.UI.Search"] = Chinese ? "搜索车辆资产" : "Search vehicle assets",
            ["RouteFilter.UI.Assets"] = Chinese ? "车辆资产" : "Vehicle assets",
            ["RouteFilter.UI.Selected"] = Chinese ? "已选择" : "selected",
            ["RouteFilter.UI.All"] = Chinese ? "全选" : "Select all",
            ["RouteFilter.UI.None"] = Chinese ? "清空" : "Clear selection",
            ["RouteFilter.UI.Refresh"] = Chinese ? "刷新资产" : "Refresh assets",
            ["RouteFilter.UI.Empty"] = Chinese ? "没有匹配的车辆资产" : "No matching vehicle assets",
        };
    }

    public void Unload() { }
}

internal sealed class LocaleEN : LocaleBase { public LocaleEN(Setting setting) : base(setting) { } protected override bool Chinese => false; }
internal sealed class LocaleZH : LocaleBase { public LocaleZH(Setting setting) : base(setting) { } protected override bool Chinese => true; }
