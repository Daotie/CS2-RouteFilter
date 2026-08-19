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

    [SettingsUIKeyboardBinding(BindingKeyboard.N, Mod.ToggleToolAction, ctrl: true, shift: true)]
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
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "显示或隐藏 RouteFilter 面板" : "Show or hide the RouteFilter panel",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "默认快捷键为 Ctrl+Shift+N，也可以点击游戏左上角按钮。" : "The default shortcut is Ctrl+Shift+N; you can also use the top-left game UI button.",
            [Setting.GetBindingMapLocaleID()] = "RouteFilter",
            [Setting.GetBindingKeyLocaleID(Mod.ToggleToolAction)] = Chinese ? "显示或隐藏 RouteFilter 面板" : "Show or hide the RouteFilter panel",
            [Setting.GetBindingKeyLocaleID(Mod.ApplyAction)] = Chinese ? "应用限制" : "Apply restrictions",
            [Setting.GetBindingKeyLocaleID(Mod.ClearAction)] = Chinese ? "清除限制" : "Clear restrictions",
            ["RouteFilter.UI.Title"] = Chinese ? "路线通行筛选" : "RouteFilter",
            ["RouteFilter.UI.Node"] = Chinese ? "节点" : "Node",
            ["RouteFilter.UI.Segment"] = Chinese ? "整段路段" : "Segment",
            ["RouteFilter.UI.Search"] = Chinese ? "搜索车辆资产" : "Search vehicle assets",
            ["RouteFilter.UI.Empty"] = Chinese ? "没有匹配的车辆资产" : "No matching vehicle assets",
            ["RouteFilter.UI.ForbiddenTitle"] = Chinese ? "禁行资产" : "Forbidden assets",
            ["RouteFilter.UI.ForbiddenHint"] = Chinese ? "选中的资产将被禁止通行。" : "Selected assets will be blocked.",
            ["RouteFilter.UI.ForbiddenCount"] = Chinese ? "项禁行" : "forbidden",
            ["RouteFilter.UI.RoadAssets"] = Chinese ? "道路车辆" : "Road vehicles",
            ["RouteFilter.UI.RailAssets"] = Chinese ? "轨道车辆" : "Rail vehicles",
            ["RouteFilter.UI.MixedAssets"] = Chinese ? "道路与轨道车辆" : "Road and rail vehicles",
            ["RouteFilter.UI.HoverTarget"] = Chinese ? "将鼠标移到目标上以筛选资产" : "Hover a target to filter assets",
            ["RouteFilter.UI.MaxSpeed"] = Chinese ? "最高速度" : "Maximum speed",
            ["RouteFilter.UI.Acceleration"] = Chinese ? "加速度" : "Acceleration",
            ["RouteFilter.UI.Braking"] = Chinese ? "制动减速度" : "Braking",
            ["RouteFilter.UI.HoverInfo"] = Chinese ? "将鼠标移到资产上查看基础参数。" : "Hover an asset to view its base parameters.",
            ["RouteFilter.UI.ForbidAll"] = Chinese ? "全部资产禁行" : "Forbid all assets",
            ["RouteFilter.UI.AllowAll"] = Chinese ? "全部资产放行" : "Allow all assets",
            ["RouteFilter.UI.NodeSelected"] = Chinese ? "已选中节点" : "Node selected",
            ["RouteFilter.UI.SegmentSelected"] = Chinese ? "已选中路段" : "Segment selected",
            ["RouteFilter.UI.SelectTarget"] = Chinese ? "请先在地图上单击节点或路段" : "Click a node or segment on the map first",
            ["RouteFilter.UI.SelectionHint"] = Chinese ? "左键选中，右键取消选中。" : "Left-click selects; right-click cancels the selection.",
            ["RouteFilter.UI.ApplyToTarget"] = Chinese ? "应用到所选目标" : "Apply list to selected target",
            ["RouteFilter.UI.ClearTarget"] = Chinese ? "清除目标限制" : "Clear target restrictions",
            ["RouteFilter.UI.CancelTarget"] = Chinese ? "取消选中" : "Cancel selection",
        };
    }

    public void Unload() { }
}

internal sealed class LocaleEN : LocaleBase { public LocaleEN(Setting setting) : base(setting) { } protected override bool Chinese => false; }
internal sealed class LocaleZH : LocaleBase { public LocaleZH(Setting setting) : base(setting) { } protected override bool Chinese => true; }
