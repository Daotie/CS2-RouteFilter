using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace RouteFilter;

[FileLocation(Mod.Id)]
[SettingsUIGroupOrder(kGeneralGroup)]
[SettingsUIShowGroupName(kGeneralGroup)]
[SettingsUIKeyboardAction(Mod.ToggleToolAction, ActionType.Button, usages: new[] { Usages.kMenuUsage })]
[SettingsUIMouseAction(Mod.ApplyAction, ActionType.Button, usages: new[] { Usages.kMenuUsage })]
[SettingsUIMouseAction(Mod.ClearAction, ActionType.Button, usages: new[] { Usages.kMenuUsage })]
public sealed class Setting : ModSetting
{
    public const string kSection = "Main";
    public const string kGeneralGroup = "General";

    public Setting(IMod mod) : base(mod) => SetDefaults();



    [SettingsUISection(kSection, kGeneralGroup)] public bool EnableRestrictionBadges { get; set; }

    [SettingsUIButton]
    [SettingsUIDisableByCondition(typeof(Setting), nameof(ResetUnavailable))]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool RunLeaseProbe { set => Mod.RequestLeaseProbe(1); }

    [SettingsUIButton]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool ReportLeaseProbe { set => Mod.RequestLeaseProbe(2); }

    [SettingsUIButton]
    [SettingsUIConfirmation("RouteFilter.Settings.ResetConfirmation")]
    [SettingsUIDisableByCondition(typeof(Setting), nameof(ResetUnavailable))]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool ResetRouteFilter
    {
        set => Mod.RequestReset();
    }

    [SettingsUIKeyboardBinding(BindingKeyboard.N, Mod.ToggleToolAction, ctrl: true, shift: true)]
    [SettingsUISection(kSection, kGeneralGroup)]
    public ProxyBinding ToggleToolBinding { get; set; }

    [SettingsUIMouseBinding(BindingMouse.Left, Mod.ApplyAction)] [SettingsUIHidden]
    public ProxyBinding ApplyBinding { get; set; }

    [SettingsUIMouseBinding(BindingMouse.Right, Mod.ClearAction)] [SettingsUIHidden]
    public ProxyBinding ClearBinding { get; set; }

    public bool ResetUnavailable() => Game.SceneFlow.GameManager.instance.gameMode != Game.GameMode.Game;

    public override void SetDefaults()
    {
        EnableRestrictionBadges = true;
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
            [Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup)] = Chinese ? "常规" : "General",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRestrictionBadges))] = Chinese ? "限制标记" : "Restriction badges",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableRestrictionBadges))] = Chinese ? "在受限目标上方显示视觉标记。" : "Shows visual badges above restricted targets.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.RunLeaseProbe))] = Chinese ? "1D：对选中目标尝试一次 lease" : "1D: attempt one lease on selected target",
            [Setting.GetOptionDescLocaleID(nameof(Setting.RunLeaseProbe))] = Chinese ? "仅供小规模测试。先选择目标并应用限制，再取消暂停。只接受当帧已校准的 Safe；Unknown/Unsafe 不修改车道，不重试，不请求绕行。" : "Small-scale test only. Select a target, apply restrictions, then unpause. Accepts only a fresh calibrated Safe verdict; Unknown/Unsafe leave lanes unchanged. No retries or reroute requests.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ReportLeaseProbe))] = Chinese ? "1D：记录 lease 测试计数" : "1D: log lease probe counters",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ReportLeaseProbe))] = Chinese ? "按需记录创建、恢复、恢复冲突与拒绝计数；不会强制等待未完成的任务。" : "Logs admissions, restorations, restore conflicts and rejections on request; does not force unfinished jobs to complete.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ResetRouteFilter))] = Chinese ? "重置 RouteFilter" : "Reset RouteFilter",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ResetRouteFilter))] = Chinese ? "清除 RouteFilter 保存的限制、选择和运行时缓存。不会清除游戏或其他模组创建的道路阻塞。" : "Clears RouteFilter restrictions, selections, and runtime caches. It does not clear road blockages created by the game or other mods.",
            ["RouteFilter.Settings.ResetConfirmation"] = Chinese ? "这将移除当前城市中的所有 RouteFilter 通行限制，以及能够安全识别的 RouteFilter 运行时状态。此操作无法撤销。无法确认归属的游戏原生或其他模组状态不会被修改。" : "This removes all RouteFilter restrictions and safely identifiable RouteFilter runtime state in this city. This cannot be undone. Vanilla or other mod state with uncertain ownership will not be modified.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "显示或隐藏 RouteFilter 面板" : "Show or hide the RouteFilter panel",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding))] = Chinese ? "默认快捷键为 Ctrl+Shift+N，也可以点击游戏左上角按钮。" : "The default shortcut is Ctrl+Shift+N; you can also use the top-left game UI button.",
            [Setting.GetBindingMapLocaleID()] = "RouteFilter",
            [Setting.GetBindingKeyLocaleID(Mod.ToggleToolAction)] = Chinese ? "显示或隐藏 RouteFilter 面板" : "Show or hide the RouteFilter panel",
            [Setting.GetBindingKeyLocaleID(Mod.ApplyAction)] = Chinese ? "应用限制" : "Apply restrictions",
            [Setting.GetBindingKeyLocaleID(Mod.ClearAction)] = Chinese ? "清除限制" : "Clear restrictions",
            ["RouteFilter.UI.Title"] = "RouteFilter",
            ["RouteFilter.UI.Node"] = Chinese ? "节点" : "Node",
            ["RouteFilter.UI.Segment"] = Chinese ? "路段" : "Segment",
            ["RouteFilter.UI.Search"] = Chinese ? "搜索车辆资产……" : "Search assets...",
            ["RouteFilter.UI.Empty"] = Chinese ? "没有匹配的车辆资产" : "No matching vehicle assets",
            ["RouteFilter.UI.ForbiddenTitle"] = Chinese ? "禁行资产" : "Forbidden assets",
            ["RouteFilter.UI.ForbiddenHint"] = Chinese ? "已选资产将被禁止通行" : "Selected assets are forbidden",
            ["RouteFilter.UI.ForbiddenCount"] = Chinese ? "项禁行" : "forbidden",
            ["RouteFilter.UI.RoadAssets"] = Chinese ? "道路车辆" : "Road vehicles",
            ["RouteFilter.UI.RailAssets"] = Chinese ? "轨道车辆" : "Rail vehicles",
            ["RouteFilter.UI.MixedAssets"] = Chinese ? "道路与轨道车辆" : "Road and rail vehicles",
            ["RouteFilter.UI.HoverTarget"] = Chinese ? "车辆资产" : "Vehicle assets",
            ["RouteFilter.UI.MaxSpeed"] = Chinese ? "最高速度" : "Maximum speed",
            ["RouteFilter.UI.Acceleration"] = Chinese ? "加速度" : "Acceleration",
            ["RouteFilter.UI.Braking"] = Chinese ? "制动减速度" : "Braking",
            ["RouteFilter.UI.HoverInfo"] = Chinese ? "将鼠标移到资产上查看基础参数。" : "Hover an asset to view its base parameters.",
            ["RouteFilter.UI.ForbidAll"] = Chinese ? "全部禁止" : "Forbid All",
            ["RouteFilter.UI.AllowAll"] = Chinese ? "全部允许" : "Allow All",
            ["RouteFilter.UI.NodeSelected"] = Chinese ? "已选中节点" : "Node selected",
            ["RouteFilter.UI.SegmentSelected"] = Chinese ? "已选中路段" : "Segment selected",
            ["RouteFilter.UI.SelectTarget"] = Chinese ? "请在地图上选择节点或路段" : "Select a node or segment on the map",
            ["RouteFilter.UI.ApplyToTarget"] = Chinese ? "应用" : "Apply",
            ["RouteFilter.UI.ClearTarget"] = Chinese ? "清除" : "Clear",
            ["RouteFilter.UI.CancelTarget"] = Chinese ? "取消选中" : "Cancel selection",
            ["RouteFilter.UI.Close"] = Chinese ? "关闭" : "Close",
            ["RouteFilter.UI.RefreshAssets"] = Chinese ? "刷新" : "Refresh",
            ["RouteFilter.UI.Reset"] = Chinese ? "重置 RouteFilter" : "Reset RouteFilter",
            ["RouteFilter.UI.ResetCompleted"] = Chinese ? "重置已完成。归属不明的车道和寻路状态未被修改。" : "Reset completed. Unknown lane and path state was left unchanged.",
            ["RouteFilter.UI.ConfirmReset"] = Chinese ? "确认重置" : "Confirm reset",
            ["RouteFilter.UI.Cancel"] = Chinese ? "取消" : "Cancel",
            ["RouteFilter.UI.Select"] = Chinese ? "选择" : "Select",
            ["RouteFilter.UI.Trailer"] = Chinese ? "挂接车辆" : "Trailer",
            ["RouteFilter.UI.Expand"] = Chinese ? "展开" : "Expand",
            ["RouteFilter.UI.Collapse"] = Chinese ? "收起" : "Collapse",
        };
    }

    public void Unload() { }
}

internal sealed class LocaleEN : LocaleBase { public LocaleEN(Setting setting) : base(setting) { } protected override bool Chinese => false; }
internal sealed class LocaleZH : LocaleBase { public LocaleZH(Setting setting) : base(setting) { } protected override bool Chinese => true; }
