using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace RouteFilterCleanup
{

[FileLocation(Mod.Id)]
[SettingsUIGroupOrder(kGeneralGroup)]
[SettingsUIShowGroupName(kGeneralGroup)]
public sealed class Setting : ModSetting
{
    public const string kSection = "Main";
    public const string kGeneralGroup = "Cleanup";
    public Setting(IMod mod) : base(mod) => SetDefaults();

    [SettingsUIButton]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool ScanSuspiciousLanes
    {
        set => Mod.RequestScan();
    }

    [SettingsUIButton]
    [SettingsUIConfirmation(null, "确认清除可识别的 RouteFilter 限制和遗留状态？此操作无法撤销。 / Remove identifiable RouteFilter restrictions and legacy state? This cannot be undone.")]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool RunSafeCleanup
    {
        set => Mod.RequestCleanup();
    }

    [SettingsUIButton]
    [SettingsUIConfirmation(null, "将一次性刷新全城道路及车道寻路图，并让所有道路车辆重新定位车道和寻路。可能出现明显卡顿，请先备份存档。不会删除道路或改动车道阻塞。确认执行？ / Rebuild the city road and lane path graph, then refresh all road vehicles together. Back up the save first. Roads and lane blockage are not deleted or changed. Continue?")]
    [SettingsUISection(kSection, kGeneralGroup)]
    public bool RefreshRoadNetworkAndPaths
    {
        set => Mod.RequestNetworkRefresh();
    }

    public override void SetDefaults() { }
}

internal abstract class LocaleBase : IDictionarySource
{
    protected readonly Setting Setting;
    protected LocaleBase(Setting setting) => Setting = setting;
    protected abstract bool Chinese { get; }
    public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
    {
        var title = Chinese ? "RouteFilter 存档清理" : "RouteFilter Save Cleanup";
        var group = Chinese ? "安全清理" : "Safe cleanup";
        var button = Chinese ? "执行安全清理" : "Run safe cleanup";
        var confirm = Chinese
            ? "这将删除当前城市中能够明确识别为 RouteFilter 所属的限制数据和遗留运行时状态。无法确认归属的原版或其他模组状态不会被修改。建议先备份存档。"
            : "This removes restriction data and legacy runtime state that can be identified as RouteFilter-owned. Vanilla or other-mod state with uncertain ownership will not be modified. Back up the save first.";
        return new Dictionary<string, string>
        {
            [Setting.GetSettingsLocaleID()] = title,
            [Setting.GetOptionTabLocaleID(Setting.kSection)] = group,
            [Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup)] = group,
            [Setting.GetOptionLabelLocaleID(nameof(Setting.ScanSuspiciousLanes))] = Chinese ? "扫描异常车道" : "Scan suspicious lanes",
            [Setting.GetOptionDescLocaleID(nameof(Setting.ScanSuspiciousLanes))] = Chinese ? "只读比较同一道路各车道的车辆速度、LaneObject、CurrentLane 和 blockage 状态，并将候选写入日志。" : "Read-only comparison of vehicle speed, LaneObject, CurrentLane, and blockage state across sibling lanes; candidates are written to the log.",
            [Setting.GetOptionLabelLocaleID(nameof(Setting.RunSafeCleanup))] = button,
            [Setting.GetOptionDescLocaleID(nameof(Setting.RunSafeCleanup))] = Chinese ? "清除 RouteFilter 限制、保存恢复记录和明确属于 RouteFilter 的旧运行时组件。" : "Remove RouteFilter restrictions, pending restore records, and clearly owned legacy runtime components.",
            ["RouteFilterCleanup.Settings.Confirmation"] = confirm,
            [Setting.GetOptionLabelLocaleID(nameof(Setting.RefreshRoadNetworkAndPaths))] = Chinese ? "重建全城道路与车辆寻路" : "Rebuild city roads and vehicle paths",
            [Setting.GetOptionDescLocaleID(nameof(Setting.RefreshRoadNetworkAndPaths))] = Chinese ? "保留道路实体，一次提交全城道路更新；原生图稳定后重新发布所有道路车道，并在同一帧请求全部道路车辆重新定位车道与寻路。不会写入车道阻塞字段。" : "Preserve road entities, submit every road for native update, republish all road-owned lanes, then request all road vehicles to reattach and repath in one simulation tick. Does not write lane blockage fields."
        };
    }
    public void Unload() { }
}
internal sealed class LocaleEN : LocaleBase { public LocaleEN(Setting s) : base(s) { } protected override bool Chinese => false; }
internal sealed class LocaleZH : LocaleBase { public LocaleZH(Setting s) : base(s) { } protected override bool Chinese => true; }
}
