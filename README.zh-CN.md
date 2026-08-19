<p align="center">
  <a href="README.md">English</a> · <strong>简体中文</strong>
</p>

<p align="center">
  <!-- 可替换发布 Logo 位置：assets/branding/routefilter-logo.png -->
  <img src="assets/branding/routefilter-logo.png" width="240" alt="RouteFilter 标志">
</p>

# RouteFilter 路线通行筛选

为 **《都市：天际线 II》** 道路与轨道路网提供精确到车辆资产的通行控制。

![版本](https://img.shields.io/badge/版本-1.0.0-2d8b70)
![状态](https://img.shields.io/badge/状态-正式版-1976d2)
![许可证](https://img.shields.io/badge/许可证-GPL--3.0--only-blue)

RouteFilter 可控制具体车辆资产能否通过某个路网节点或整段道路、有轨电车轨道、铁路、地铁线路。匹配车辆会在穿过受限目标前被拦截；当路网存在可行替代路线时，模组会请求重新寻路。

## 主要功能

- 按单个车辆资产限制通行，而非仅按宽泛交通类别处理。
- 为指定节点或完整路段分别保存独立的禁行清单。
- 支持道路车辆、过境交通、有轨电车、火车、地铁、车头、车厢及已识别挂车。
- 自动发现本体、内容包及当前可用的自定义车辆预制资产。
- 根据所选道路或轨道目标，仅显示兼容的车辆资产。
- 支持资产搜索、分页和列表滚动，应用按钮始终保留在同一页面。
- 悬浮资产时查看以 km/h 显示的最高速度、加速度和制动减速度。
- 对已识别车头或牵引车辆分组显示车厢、挂车，并支持逐项控制。
- 节点和路段具有清晰的悬浮线框及独立的选中高亮。
- 切换目标时读取该目标自己的禁行清单，待应用修改不会静默作用于全图。
- 将匹配车辆的重新寻路请求接入游戏寻路数据；没有替代路线时，车辆也不能直接穿过受限目标。
- 限制数据随存档保存，并根据游戏语言自动切换英文或简体中文。

## 游戏内功能介绍

### 1. 精确选择路网目标

选择“节点”或“整段路段”，然后左键单击高亮目标；右键可取消选择。打开面板时选择工具会同步启用，不再需要额外点击“打开工具”。

<!-- 截图占位：assets/screenshots/01-target-selection.png -->
<!-- ![选择高亮的道路或轨道目标](assets/screenshots/01-target-selection.png) -->

### 2. 选择具体车辆资产

选中的条目就是将被**禁止通行**的资产。道路目标只显示道路车辆，轨道目标只显示兼容轨道资产。悬浮条目可查看基础参数；展开已识别编组后，可分别选择车头、车厢或挂车。

<!-- 截图占位：assets/screenshots/02-asset-catalog.png -->
<!-- ![筛选并选择具体车辆资产](assets/screenshots/02-asset-catalog.png) -->

### 3. 检查并应用

“全部资产禁行”和“全部资产放行”只修改待应用清单。只有点击“应用到所选目标”后才会写入地图。切换目标时会加载该目标自己保存的清单；“清除目标限制”会移除所选目标上的 RouteFilter 数据。

<!-- 截图占位：assets/screenshots/03-apply-restriction.png -->
<!-- ![将禁行清单应用到一个指定目标](assets/screenshots/03-apply-restriction.png) -->

### 4. 车辆拦截与绕行

RouteFilter 会检查当前车道、前方导航车道、底层路径元素、节点端点以及车辆编组内每个已识别预制资产。命中限制后，模组会短暂将目标标记为寻路不可用并使该车辆当前路径失效；存在替代路线时车辆可绕行，不存在时车辆会被阻止继续正常穿过，并可能稍后重试。

<!-- 截图占位：assets/screenshots/04-rerouting-result.png -->
<!-- ![受限车辆选择替代路线](assets/screenshots/04-rerouting-result.png) -->

## 安装

### Paradox Mods

Paradox Mods 页面上线后，订阅 **RouteFilter** 并将其加入当前播放集；如果播放集提示重启，请重启游戏。

### 手动安装

1. 从 [GitHub Releases](https://github.com/Daotie/CS2-RouteFilter/releases) 下载最新压缩包。
2. 解压到《都市：天际线 II》本地模组目录。
3. 在当前播放集启用 **RouteFilter**。
4. 替换旧 DLL 后请重启游戏。

更改代码模组组合前，建议备份重要城市存档。

## 快速使用

1. 点击游戏左上角的 RouteFilter 按钮，或按 `Ctrl+Shift+N`。
2. 选择“节点”或“整段路段”。
3. 左键选择要编辑的路网目标。
4. 选中需要禁止通行的车辆资产。
5. 点击“应用到所选目标”。
6. 随时重新选择目标以检查或修改其已保存清单。

快捷键可在游戏设置中重新绑定。关闭面板时，选择工具也会同步关闭。

## 可配置项

| 设置 | 默认值 | 用途 |
| --- | --- | --- |
| 逐资产限制 | 开启 | 在受限节点或路段按准确预制资产匹配执行限制。 |
| 应急车辆保护 | 关闭 | 即使选中了对应资产，也放行警车、救护车和消防车。 |
| 前瞻车道数 | 3 | 控制车辆提前多少段导航车道检测限制。 |
| 显示或隐藏面板 | `Ctrl+Shift+N` | 同时打开或关闭 RouteFilter 面板及选择工具。 |

## 存档数据与升级

RouteFilter `1.0.0` 使用公开测试阶段引入的逐资产 V1 存档结构。每个受限目标保存准确的车辆预制实体引用；使用 `0.4.0-beta.1` 或 `0.5.0-beta.1` 创建的限制沿用相同结构。

重建、替换或删除道路、轨道路段会使游戏创建新实体，与原目标关联的限制可能因此丢失。大规模改造路网后请重新检查限制。

## 重要行为与已知限制

- 资产名称来自游戏或资产作者提供的技术名称，不一定存在本地化显示名。
- 仅当游戏明确提供固定挂车或动车组关系时，界面才会显示车头—车厢分组。
- 固定公共交通线路不一定存在可行绕行路线。没有替代路线时，受影响车辆会停车并可能重试；RouteFilter 不会自动重绘玩家创建的公交或轨道线路。
- 在寻路屏障生效的短暂窗口内，其他同时请求路径的车辆也可能避开所选目标。
- 无法保证兼容所有替换车辆导航、寻路或路网实体的模组。报告冲突时请使用最小播放集并附上日志。

## 兼容性与支持

RouteFilter 使用《都市：天际线 II》官方代码模组工具链，无需额外框架模组。遇到问题时，请先使用最新版本及尽可能精简的播放集复现，并提供游戏版本、RouteFilter 版本、复现步骤、当前交通/路网模组和 `Player.log`。

- 使用帮助与问题报告：[SUPPORT.md](SUPPORT.md)
- 安全问题披露：[SECURITY.md](SECURITY.md)
- 版本历史：[CHANGELOG.md](CHANGELOG.md)
- 贡献规范：[CONTRIBUTING.md](CONTRIBUTING.md)

## 开发

需要《都市：天际线 II》官方模组工具链、.NET SDK，以及 Node.js 18 或更高版本。

```powershell
dotnet build RouteFilter.csproj -c Release
cd UI
npm ci
$env:ROUTEFILTER_OUTPUT_DIR = (Join-Path (Get-Location) "build")
npm run build
npm audit --omit=dev
```

官方 Paradox Mods 元数据和发布配置档位于 `Properties`。完整发布检查表见 [RELEASING.md](RELEASING.md)。

## 许可证与归属说明

版权所有 © 2026 Daotie。RouteFilter 采用 [GNU General Public License v3.0 only](LICENSE) 许可。

《都市：天际线 II》及相关名称是其各自权利人的商标。RouteFilter 是独立社区项目，与 Colossal Order 或 Paradox Interactive 不存在隶属、认可或官方合作关系。
