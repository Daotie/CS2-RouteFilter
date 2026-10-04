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

    [SettingsUISection(kSection, kGeneralGroup)] public bool ShowRoadRestrictionSigns { get; set; }
    [SettingsUIHidden] public string FavoriteAssetIds { get; set; }
    [SettingsUIHidden] public string RecentAssetIds { get; set; }
    [SettingsUIHidden] public string CustomRoadSignPrefab { get; set; }
    [SettingsUIHidden] public string RoadSignPrefabMode { get; set; }

    /// <summary>Master switch for the road enforcement backend.</summary>
    [SettingsUISection(kSection, kGeneralGroup)] public bool EnableRoadEnforcement { get; set; }

    /// <summary>Master switch for the rail enforcement backend.</summary>
    [SettingsUISection(kSection, kGeneralGroup)] public bool EnableRailEnforcement { get; set; }

    /// <summary>
    /// Emergency services never get rerouted. Connected to both backends through
    /// <c>EnforcementPolicy.IsExempt</c>, filtered before the expensive matching pipeline.
    /// </summary>
    [SettingsUISection(kSection, kGeneralGroup)] public bool EmergencyProtection { get; set; }

    /// <summary>
    /// Conservative budget, in seconds, for vanilla to enqueue, run and apply one pathfind plus the
    /// vehicle's own reaction. Larger values refuse more often; the safe direction is to refuse.
    /// </summary>
    [SettingsUIHidden]
    public float RerouteLatencySeconds { get; set; }

    /// <summary>Extra distance margin in metres added to the modelled braking distance.</summary>
    [SettingsUIHidden]
    public float RerouteUncertaintyMetres { get; set; }

    /// <summary>Logs one aggregated RouteFilter statistics line per interval.</summary>
    [SettingsUISection(kSection, kGeneralGroup)] public bool VerboseDiagnostics { get; set; }

    [SettingsUIButton]
    [SettingsUIDisableByCondition(typeof(Setting), nameof(ResetUnavailable))]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool LogDiagnostics { set => Mod.RequestDiagnosticsReport(); }

    [SettingsUIButton]
    [SettingsUIDisableByCondition(typeof(Setting), nameof(ResetUnavailable))]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool TestNativeProtocol { set => Mod.RequestNativeProtocolTest(); }

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
        ShowRoadRestrictionSigns = true;
        CustomRoadSignPrefab = string.Empty;
        RoadSignPrefabMode = "AUTO";
        EnableRoadEnforcement = true;
        EnableRailEnforcement = true;
        EmergencyProtection = true;
        RerouteLatencySeconds = 2.4f;
        RerouteUncertaintyMetres = 5f;
        VerboseDiagnostics = false;
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
            [Setting.GetSettingsLocaleID()] = "RouteFilter",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ShowRoadRestrictionSigns))] = Chinese ? "显示道路禁行标志" : "Show Road Restriction Signs",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ShowRoadRestrictionSigns))] = Chinese ? "在已应用的道路禁行入口两侧显示标志牌。样式可在 RouteFilter 面板选择。" : "Shows signs beside applied restricted road entries. Choose the style in the RouteFilter panel.",
            ["RouteFilter.UI.LibraryAll"] = Chinese ? "全部" : "All",
            ["RouteFilter.UI.LibraryFavorites"] = Chinese ? "收藏" : "Favorites",
            ["RouteFilter.UI.LibraryRecent"] = Chinese ? "最近" : "Recent",
            ["RouteFilter.UI.LibraryCopy"] = Chinese ? "复制" : "Copy",
            ["RouteFilter.UI.LibraryPaste"] = Chinese ? "粘贴" : "Paste",
            ["RouteFilter.UI.LibraryFavoriteToggle"] = Chinese ? "切换收藏" : "Toggle favorite",
            ["RouteFilter.UI.RoadSignSearch"] = Chinese ? "搜索 prefab……" : "Search prefabs…",
            ["RouteFilter.UI.RoadSignStyle"] = Chinese ? "道路禁行标志样式" : "Road Restriction Sign Prefab",
            ["RouteFilter.UI.RoadSignAuto"] = Chinese ? "自动 — 匹配道路主题" : "Auto — Match Road Theme",
            ["RouteFilter.UI.RoadSignResolved"] = Chinese ? "自动解析" : "Auto resolved",
            ["RouteFilter.UI.RoadSignUnavailable"] = Chinese ? "没有兼容标志牌；禁行仍然有效" : "No compatible sign; restrictions remain active",
            ["RouteFilter.UI.RoadSignMissing"] = Chinese ? "自定义标志未加载；暂用自动样式" : "Custom sign unavailable; using Auto temporarily",
            [Setting.GetOptionTabLocaleID(Setting.kSection)] = Chinese ? "主要设置" : "General",
            [Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup)] = Chinese ? "常规" : "General",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRestrictionBadges))] = Chinese ? "限制标记" : "Restriction badges",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableRestrictionBadges))] = Chinese ? "在受限目标上方显示视觉标记。" : "Shows visual badges above restricted targets.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRoadEnforcement))] = Chinese ? "道路禁行（测试版）" : "Road enforcement (test build)",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableRoadEnforcement))] = Chinese ? "为距离足够且导航明确的指定车辆请求一次原生绕行，仅在该请求中排除受限目标；无替代路线时放行。实际游戏行为仍待验证。" : "Requests one native reroute for a sufficiently distant, unambiguous restricted vehicle. Excludes the target only for that request; falls back when no alternative exists. Gameplay verification pending.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRailEnforcement))] = Chinese ? "轨道禁行（测试版）" : "Rail enforcement (test build)",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableRailEnforcement))] = Chinese ? "按编组请求一次原生绕行，在该轨道寻路请求中排除受限目标。保持原生目的地与线路站点；无法绕行时放行，不修改 TrackLane 阻塞。实际游戏行为仍待验证。" : "Requests one native reroute per consist, excluding the target within that track query. Preserves native destinations and line stops; falls back if no detour exists. No TrackLane blockage writes. Gameplay verification pending.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EmergencyProtection))] = Chinese ? "紧急车辆豁免" : "Emergency vehicle exemption",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EmergencyProtection))] = Chinese ? "警车、消防车、救护车和灵车永不被重新规划。" : "Police, fire, ambulance and hearse vehicles are never rerouted.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.RerouteLatencySeconds))] = Chinese ? "绕行延迟预算（秒）" : "Reroute latency budget (seconds)",
            [Setting.GetOptionDescLocaleID(nameof(Setting.RerouteLatencySeconds))] = Chinese ? "重新规划被允许时必须剩余的距离，按此时间预算计算。数值越大越保守，越常放行车辆。" : "Distance a vehicle must still have left, derived from this time budget. Higher values are more conservative and let more vehicles through.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.RerouteUncertaintyMetres))] = Chinese ? "安全余量（米）" : "Safety margin (metres)",
            [Setting.GetOptionDescLocaleID(nameof(Setting.RerouteUncertaintyMetres))] = Chinese ? "在制动距离之外额外保留的距离。" : "Extra distance kept in addition to the modelled braking distance.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.VerboseDiagnostics))] = Chinese ? "详细诊断日志" : "Verbose diagnostics",
            [Setting.GetOptionDescLocaleID(nameof(Setting.VerboseDiagnostics))] = Chinese ? "每 300 个模拟帧记录一条聚合统计行。仅用于排查问题，正常游玩时保持关闭。" : "Logs one aggregated statistics line every 300 simulation frames. Intended for troubleshooting; keep it off during normal play.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.LogDiagnostics))] = Chinese ? "记录当前统计" : "Log current statistics",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.TestNativeProtocol))] = Chinese ? "测试存档原生协议（隔离内存）" : "Test native save protocol (isolated memory)",
            [Setting.GetOptionDescLocaleID(nameof(Setting.TestNativeProtocol))] = Chinese ? "使用临时内存验证实体映射、中文字符串和数据块边界，并记录结果。不读取或修改城市；此测试不等同于完整存档载入验证。" : "Checks entity remapping, Unicode strings and block alignment in temporary memory and logs the result. Does not read or modify the city; this is not a complete city save/load test.",
            [Setting.GetOptionDescLocaleID(nameof(Setting.LogDiagnostics))] = Chinese ? "按需记录一次工作负载、候选、安全判定、绕行请求与车道修改计数。不会强制等待未完成的任务。" : "Logs workload, candidate, safety, reroute and lane mutation counters on request; never force-finishes a running job.",
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
            ["RouteFilter.UI.EntryDirections"] = Chinese ? "限制入口" : "Restricted entries",
            ["RouteFilter.UI.AllEntries"] = Chinese ? "全部入口" : "All entries",
            ["RouteFilter.UI.EntryDirectionsUnavailable"] = Chinese ? "全部入口（不可分组）" : "All entries (unavailable)",
            ["RouteFilter.UI.EntryDirectionsHint"] = Chinese ? "点击道路上的控制条切换禁行入口，然后应用。亮色表示禁行，暗色表示允许。" : "Click approach bars on the road, then Apply. Bright bars restrict entry; muted bars allow it.",
            ["RouteFilter.UI.EntryDirectionsUnsupported"] = Chinese ? "入口无法可靠区分；保持全部入口禁行。" : "Entries cannot be separated reliably; all-entry restrictions remain active.",
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
            ["RouteFilter.UI.PersistenceLocked"] = Chinese ? "存档数据不兼容或损坏，编辑已锁定。重置将清除 RouteFilter 配置。" : "Save data is incompatible or damaged. Editing is locked; Reset removes RouteFilter configuration.",
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
