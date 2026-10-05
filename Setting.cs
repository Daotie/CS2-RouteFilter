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



    [SettingsUIHidden] public bool EnableRestrictionBadges { get; set; }

    [SettingsUISection(kSection, kGeneralGroup)] public bool ShowRoadRestrictionSigns { get; set; }
    [SettingsUIHidden] public string FavoriteAssetIds { get; set; }
    [SettingsUIHidden] public string RecentAssetIds { get; set; }
    [SettingsUIHidden] public string UserPresets { get; set; }
    [SettingsUIHidden] public bool ForceSupplementaryPlate { get; set; }
    [SettingsUIHidden] public float RoadSignScale { get; set; }
    [SettingsUIHidden] public float RoadSignHeight { get; set; }
    [SettingsUIHidden] public float RoadSignLateralOffset { get; set; }
    [SettingsUIHidden] public float RoadSignLongitudinalOffset { get; set; }
    [SettingsUIHidden] public float RoadSignRotation { get; set; }
    [SettingsUIHidden] public float RoadPlateSpacing { get; set; }
    [SettingsUIHidden] public string CustomRoadSignPrefab { get; set; }
    [SettingsUIHidden] public string SignageProfile { get; set; }
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
        EnableRestrictionBadges = false;
        ShowRoadRestrictionSigns = true;
        CustomRoadSignPrefab = string.Empty;
        RoadSignPrefabMode = "AUTO";
        SignageProfile = "AUTO";
        RoadSignScale = 1f;
        RoadSignHeight = 0f;
        RoadSignLateralOffset = 0f;
        RoadSignLongitudinalOffset = 0f;
        RoadSignRotation = 0f;
        RoadPlateSpacing = .04f;
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
    protected virtual string SignLocale => Chinese ? "zh-CN" : "en-US";

    public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
    {
        var entries = new Dictionary<string, string>
        {
            [Setting.GetSettingsLocaleID()] = "RouteFilter",
            ["RouteFilter.UI.RangeStart"] = Chinese ? "请选择起点路段" : "Select the start segment",
            ["RouteFilter.UI.RangeEnd"] = Chinese ? "请选择终点路段" : "Select the end segment",
            ["RouteFilter.UI.RangeDisconnected"] = Chinese ? "无法生成连接链，请重新选择终点" : "No connected chain between the segments",
            ["RouteFilter.UI.RangeReady"] = Chinese ? "连接链已准备好，请确认" : "Connected chain ready for confirmation",
            ["RouteFilter.UI.VehicleCategories"] = Chinese ? "车辆类别" : "Vehicle categories",
            ["RouteFilter.UI.SpecifiedVehicles"] = Chinese ? "指定车辆" : "Selected vehicles",
            ["RouteFilter.UI.AllVehicles"] = Chinese ? "全部道路车辆" : "All road vehicles",
            ["RouteFilter.UI.SignageProfile"] = Chinese ? "标志规范" : "Signage profile",
            ["RouteFilter.UI.Profile.AUTO"] = Chinese ? "自动" : "Auto",
            ["RouteFilter.UI.Profile.GENERIC_EUROPE"] = Chinese ? "欧洲通用" : "Generic Europe",
            ["RouteFilter.UI.Profile.CN"] = Chinese ? "中国大陆" : "Mainland China",
            ["RouteFilter.UI.Profile.UK"] = Chinese ? "英国" : "United Kingdom",
            ["RouteFilter.UI.Profile.US"] = Chinese ? "美国" : "United States",
            ["RouteFilter.UI.Profile.MIXED"] = Chinese ? "按各入口道路主题匹配" : "Resolved per entry road theme",
            ["RouteFilter.UI.ProfileFallback"] = Chinese ? "使用兼容标志" : "Using compatible sign.",
            ["RouteFilter.UI.PlateLanguage"] = Chinese ? "辅助标志文字跟随界面语言；未支持的语言回退至英语。" : "Supplementary text follows UI language; unsupported languages fall back to English.",
            ["RouteFilter.UI.Feedback.Copied"] = Chinese ? "已复制 {count} 项车辆限制。" : "Copied {count} vehicle restrictions.",
            ["RouteFilter.UI.Feedback.Pasted"] = Chinese ? "已载入 {count} 项车辆限制" : "Loaded {count} vehicle restrictions.",
            ["RouteFilter.UI.Feedback.Incompatible"] = Chinese ? "没有兼容车辆" : "No compatible vehicles.",
            ["RouteFilter.UI.Feedback.AllowAll"] = Chinese ? "已应用：允许所有兼容车辆。" : "Applied: all compatible vehicles allowed.",
            ["RouteFilter.UI.Feedback.Preset"] = Chinese ? "已载入 {count} 项车辆限制" : "Loaded {count} vehicle restrictions.",
            ["RouteFilter.UI.MapEmpty"] = Chinese ? "尚未获取到道路，请重试。" : "Road geometry is not available yet.",
            ["RouteFilter.UI.MapRefresh"] = Chinese ? "重试" : "Refresh",
            ["RouteFilter.UI.MapZoomIn"] = Chinese ? "放大" : "Zoom in",
            ["RouteFilter.UI.MapZoomOut"] = Chinese ? "缩小" : "Zoom out",
            ["RouteFilter.UI.Feedback.Applied"] = Chinese ? "已应用车辆限制" : "Vehicle restrictions applied.",
            ["RouteFilter.UI.PresetBuiltIn"] = Chinese ? "内置预设" : "Built-in presets",
            ["RouteFilter.UI.PresetMine"] = Chinese ? "我的预设" : "My presets",
            ["RouteFilter.UI.PresetActions"] = Chinese ? "预设操作" : "Preset actions",
            ["RouteFilter.UI.Back"] = Chinese ? "返回" : "Back",
            ["RouteFilter.UI.Tools"] = Chinese ? "工具" : "Tools",
            ["RouteFilter.UI.SignStyle"] = Chinese ? "道路限制标志样式" : "Road restriction sign style",
            ["RouteFilter.UI.Appearancelongitudinal"] = Chinese ? "纵向偏移" : "Longitudinal offset",
            ["RouteFilter.UI.Appearancerotation"] = Chinese ? "旋转" : "Rotation",
            ["RouteFilter.UI.AppearanceReset"] = Chinese ? "重置位置" : "Reset position",
            ["RouteFilter.UI.Finish"] = Chinese ? "完成调整" : "Finish",
            ["RouteFilter.UI.WheelStep"] = Chinese ? "步长" : "Step",
            ["RouteFilter.UI.WheelHint"] = Chinese ? "悬浮输入框时滚轮调整。" : "Scroll over an input to adjust.",
            ["RouteFilter.UI.PresetRename"] = Chinese ? "重命名" : "Rename",
            ["RouteFilter.UI.PresetTrucks"] = Chinese ? "货车" : "Trucks",
            ["RouteFilter.UI.PresetCargoTrucks"] = Chinese ? "货运货车" : "Cargo trucks",
            ["RouteFilter.UI.RangeSegments"] = Chinese ? "个路段" : "segments in preview",
            ["RouteFilter.UI.RangeApply"] = Chinese ? "应用到所选范围" : "Apply to range",
            ["RouteFilter.UI.RangeClear"] = Chinese ? "清除范围内的限制" : "Clear range restrictions",
            ["RouteFilter.UI.RangePolicy"] = Chinese ? "连接链按最少路段数确定。" : "The connected chain uses the fewest segments.",
            ["RouteFilter.UI.MapLayerroads"] = Chinese ? "道路" : "Roads",
            ["RouteFilter.UI.MapLayersegments"] = Chinese ? "受限路段" : "Restricted segments",
            ["RouteFilter.UI.MapLayernodes"] = Chinese ? "受限节点" : "Restricted nodes",
            ["RouteFilter.UI.MapLayerentries"] = Chinese ? "限制入口" : "Restricted entries",
            ["RouteFilter.UI.SignInfo"] = Chinese ? "道路限制禁令标志" : "Road restriction sign",
            ["RouteFilter.UI.EditSign"] = Chinese ? "在 RouteFilter 中编辑" : "Edit in RouteFilter",
            ["RouteFilter.UI.VehicleAssets"] = Chinese ? "车辆资产" : "Vehicle assets",
            ["RouteFilter.UI.Presets"] = Chinese ? "限制预设" : "Presets",
            ["RouteFilter.UI.Map"] = Chinese ? "限行地图" : "Restriction Map",
            ["RouteFilter.UI.Appearance"] = Chinese ? "标志位置调整" : "Sign appearance",
            ["RouteFilter.UI.Batch"] = Chinese ? "批量" : "Batch",
            ["RouteFilter.UI.ForcePlate"] = Chinese ? "强制显示辅助牌" : "Force supplementary plate",
            ["RouteFilter.UI.BatchApply"] = Chinese ? "批量应用" : "Batch apply",
            ["RouteFilter.UI.BatchClear"] = Chinese ? "批量清除" : "Batch clear",
            ["RouteFilter.UI.BatchDrag"] = Chinese ? "左键应用 · 右键清除" : "Left: apply · Right: clear",
            ["RouteFilter.UI.Brush"] = Chinese ? "批量路段限制" : "Batch segment restrictions",
            ["RouteFilter.UI.BrushApply"] = Chinese ? "按住拖选，松开应用" : "Hold to select; release to apply",
            ["RouteFilter.UI.BrushClear"] = Chinese ? "取消范围选择" : "Cancel range selection",
            ["RouteFilter.UI.BrushHint"] = Chinese ? "按住拖选路段，松开提交。" : "Hold to select road segments; release to commit.",
            ["RouteFilter.UI.PresetName"] = Chinese ? "预设名称" : "Preset name",
            ["RouteFilter.UI.PresetSave"] = Chinese ? "保存选中资产" : "Save selected assets",
            ["RouteFilter.UI.PresetOverwrite"] = Chinese ? "用当前选中资产替换" : "Replace with selected assets",
            ["RouteFilter.UI.PresetDelete"] = Chinese ? "删除" : "Delete",
            ["RouteFilter.UI.PresetMissing"] = Chinese ? "缺失资产" : "Missing assets",
            ["RouteFilter.UI.PresetUnsupported"] = Chinese ? "当前目标不支持" : "Unsupported on this target",
            ["RouteFilter.UI.PresetHint"] = Chinese ? "仅载入待应用资产，不改变目标或入口方向。准备就绪后点击应用。" : "Loads assets into the pending selection; target and entry directions stay unchanged. Press Apply when ready.",
            ["RouteFilter.UI.MapFit"] = Chinese ? "适配城市" : "Fit city",
            ["RouteFilter.UI.MapTargets"] = Chinese ? "个目标" : "targets",
            ["RouteFilter.UI.MapAssets"] = Chinese ? "个资产" : "assets",
            ["RouteFilter.UI.MapSelect"] = Chinese ? "选择受限目标" : "Select restricted target",
            ["RouteFilter.UI.MapHint"] = Chinese ? "红色目标设有限制。点击编辑，拖动背景平移地图。" : "Red targets have restrictions. Click to edit; drag the background to pan.",
            ["RouteFilter.UI.Appearancescale"] = Chinese ? "主标志缩放" : "Main sign scale",
            ["RouteFilter.UI.Appearanceheight"] = Chinese ? "高度" : "Height",
            ["RouteFilter.UI.Appearanceoffset"] = Chinese ? "横向偏移" : "Lateral offset",
            ["RouteFilter.UI.Appearancespacing"] = Chinese ? "辅助标志间距（米）" : "Plate gap (m)",
            ["RouteFilter.UI.AppearanceHint"] = Chinese ? "RF-Plate 固定为 0.800 × 0.250 × 0.020 米。语义合并后最多显示两块文字辅助标志。" : "RF-Plate stays 0.800 × 0.250 × 0.020 m. Semantic consolidation limits supplementary text to two plates.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ShowRoadRestrictionSigns))] = Chinese ? "显示道路限制禁令标志" : "Show Road Restriction Signs",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ShowRoadRestrictionSigns))] = Chinese ? "在已应用的道路禁行入口两侧显示标志牌。样式可在 RouteFilter 面板选择。" : "Shows signs beside applied restricted road entries. Choose the style in the RouteFilter panel.",
            ["RouteFilter.UI.LibraryAll"] = Chinese ? "全部" : "All",
            ["RouteFilter.UI.LibraryFavorites"] = Chinese ? "收藏" : "Favorites",
            ["RouteFilter.UI.LibraryRecent"] = Chinese ? "最近" : "Recent",
            ["RouteFilter.UI.Copy"] = Chinese ? "复制" : "Copy",
            ["RouteFilter.UI.Paste"] = Chinese ? "粘贴" : "Paste",
            ["RouteFilter.UI.Category.Bus"] = Chinese ? "公共汽车" : "Buses",
            ["RouteFilter.UI.Category.Taxi"] = Chinese ? "出租汽车" : "Taxis",
            ["RouteFilter.UI.Category.Goods"] = Chinese ? "载货汽车" : "Goods vehicles",
            ["RouteFilter.UI.Category.Emergency"] = Chinese ? "紧急车辆" : "Emergency vehicles",
            ["RouteFilter.UI.Category.Service"] = Chinese ? "市政与养护车辆" : "Service vehicles",
            ["RouteFilter.UI.Category.Rail"] = Chinese ? "轨道车辆" : "Rail vehicles",
            ["RouteFilter.UI.Category.Other"] = Chinese ? "其他道路车辆" : "Other road vehicles",
            ["RouteFilter.UI.SignTool"] = Chinese ? "交通标志" : "Traffic sign",
            ["RouteFilter.UI.LibraryCopy"] = Chinese ? "复制车辆限制" : "Copy vehicle restrictions",
            ["RouteFilter.UI.LibraryPaste"] = Chinese ? "粘贴车辆限制" : "Paste vehicle restrictions",
            ["RouteFilter.UI.LibraryFavoriteToggle"] = Chinese ? "切换收藏" : "Toggle favorite",
            ["RouteFilter.UI.RoadSignSearch"] = Chinese ? "搜索交通标志资产……" : "Search sign assets…",
            ["RouteFilter.UI.RoadSignStyle"] = Chinese ? "道路限制标志样式" : "Road restriction sign style",
            ["RouteFilter.UI.RoadSignAuto"] = Chinese ? "自动匹配道路主题" : "Auto — Match Road Theme",
            ["RouteFilter.UI.RoadSignResolved"] = Chinese ? "当前匹配" : "Auto resolved",
            ["RouteFilter.UI.RoadSignUnavailable"] = Chinese ? "暂无兼容标志" : "No compatible sign",
            ["RouteFilter.UI.RoadSignMissing"] = Chinese ? "使用自动样式" : "Using Auto style",
            [Setting.GetOptionTabLocaleID(Setting.kSection)] = Chinese ? "主要设置" : "General",
            [Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup)] = Chinese ? "常规" : "General",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRestrictionBadges))] = Chinese ? "限制标记" : "Restriction badges",
            [Setting.GetOptionDescLocaleID(nameof(Setting.EnableRestrictionBadges))] = Chinese ? "旧版显示选项；正常游玩不显示地面限制线。" : "Shows ground restriction lines at applied road entries.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRoadEnforcement))] = Chinese ? "道路通行限制（测试版）" : "Road enforcement (test build)",
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
            ["RouteFilter.UI.SelectTarget"] = Chinese ? "未选中目标" : "No target selected",
            ["RouteFilter.UI.ApplyToTarget"] = Chinese ? "应用" : "Apply",
            ["RouteFilter.UI.PersistenceLocked"] = Chinese ? "存档数据不兼容或损坏，编辑已锁定。重置将清除 RouteFilter 配置。" : "Save data is incompatible or damaged. Editing is locked; Reset removes RouteFilter configuration.",
            ["RouteFilter.UI.ClearTarget"] = Chinese ? "清除" : "Clear",
            ["RouteFilter.UI.CancelTarget"] = Chinese ? "取消选中" : "Cancel selection",
            ["RouteFilter.UI.Close"] = Chinese ? "关闭" : "Close",
            ["RouteFilter.UI.RefreshAssets"] = Chinese ? "刷新" : "Refresh",
            ["RouteFilter.UI.Reset"] = Chinese ? "重置 RouteFilter" : "Reset RouteFilter",
            ["RouteFilter.UI.ResetCompleted"] = Chinese ? "重置已完成" : "Reset completed.",
            ["RouteFilter.UI.ConfirmReset"] = Chinese ? "确认重置" : "Confirm reset",
            ["RouteFilter.UI.Cancel"] = Chinese ? "取消" : "Cancel",
            ["RouteFilter.UI.Select"] = Chinese ? "选择" : "Select",
            ["RouteFilter.UI.Trailer"] = Chinese ? "挂接车辆" : "Trailer",
            ["RouteFilter.UI.Expand"] = Chinese ? "展开" : "Expand",
            ["RouteFilter.UI.Collapse"] = Chinese ? "收起" : "Collapse",
        };
        foreach(RouteFilter.Persistence.TrafficVehicleSemantic semantic in System.Enum.GetValues(typeof(RouteFilter.Persistence.TrafficVehicleSemantic)))
        foreach(var partial in new[] {false,true})
        {
            var legend=new RouteFilter.Persistence.TrafficLegend(semantic,partial);
            entries[legend.Key]=RouteFilter.Persistence.TrafficSignLocalization.Text(legend,SignLocale);
        }
        return entries;
    }

    public void Unload() { }
}

internal sealed class LocaleEN : LocaleBase { public LocaleEN(Setting setting) : base(setting) { } protected override bool Chinese => false; }
internal sealed class LocaleZH : LocaleBase { public LocaleZH(Setting setting) : base(setting) { } protected override bool Chinese => true; }

internal sealed class LocaleENGB : LocaleBase { public LocaleENGB(Setting setting) : base(setting) { } protected override bool Chinese => false; protected override string SignLocale => "en-GB"; }
