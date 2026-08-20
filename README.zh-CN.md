<p align="center">
  <a href="README.md">English</a> · <strong>简体中文</strong>
</p>

<p align="center">
  <img src="assets/branding/routefilter-logo.png" width="240" alt="RouteFilter 标志">
</p>

<h1 align="center">RouteFilter 路线通行筛选</h1>

<p align="center">
  为 <strong>《都市：天际线 II》</strong> 道路与轨道路网提供精确到车辆资产的通行控制。
</p>

<p align="center">
  <img src="https://img.shields.io/badge/版本-1.0.5-2d8b70" alt="版本">
  <img src="https://img.shields.io/badge/状态-正式版-1976d2" alt="状态">
  <img src="https://img.shields.io/badge/许可证-GPL--3.0--only-blue" alt="许可证">
</p>

<p align="center">
  <a href="https://mods.paradoxplaza.com/mods/155839/Windows">Paradox Mods</a>
  ·
  <a href="https://forum.paradoxplaza.com/forum/threads/mod-routefilter-per-asset-access-control-for-roads-rails.1938927/">Paradox Forum</a>
  ·
  <a href="https://github.com/Daotie/CS2-RouteFilter/issues">Issues</a>
  ·
  <a href="https://discord.gg/Y9UXFCkqmD">Discord</a>
</p>

---

RouteFilter 可控制具体车辆资产能否通过某个路网节点或整段道路、有轨电车轨道、铁路、地铁线路。

匹配车辆会在穿过受限目标前被拦截；当路网存在可行替代路线时，模组会请求重新寻路。

<!-- RouteFilter 功能简介 -->
<p align="center">
  <img src="assets/showcase/P1.png" width="100%" alt="RouteFilter 功能简介与使用效果">
</p>

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

选择“节点”或“整段路段”，然后左键单击高亮目标；右键可取消选择。

<!-- 截图占位：assets/screenshots/01-target-selection.png -->
<!-- ![选择高亮的道路或轨道目标](assets/screenshots/01-target-selection.png) -->

### 2. 选择具体车辆资产

选中的条目就是将被**禁止通行**的资产。

道路目标只显示道路车辆，轨道目标只显示兼容轨道资产。悬浮条目可查看基础参数；展开已识别编组后，可分别选择车头、车厢或挂车。

<!-- 截图占位：assets/screenshots/02-asset-catalog.png -->
<!-- ![筛选并选择具体车辆资产](assets/screenshots/02-asset-catalog.png) -->

### 3. 检查并应用

“全部资产禁行”和“全部资产放行”只修改待应用清单。只有点击“应用到所选目标”后才会写入地图。

切换目标时会加载该目标自己保存的清单；“清除目标限制”会移除所选目标上的 RouteFilter 数据。

<!-- 截图占位：assets/screenshots/03-apply-restriction.png -->
<!-- ![将禁行清单应用到一个指定目标](assets/screenshots/03-apply-restriction.png) -->

### 4. 车辆拦截与绕行

RouteFilter 会检查当前车道、前方导航车道、底层路径元素、节点端点以及车辆编组内每个已识别预制资产。

命中限制后，模组会短暂将目标标记为寻路不可用并使该车辆当前路径失效；存在替代路线时车辆可绕行，不存在时车辆会被阻止继续正常穿过，并可能稍后重试。

<!-- RouteFilter 如何影响车辆寻路 -->
<p align="center">
  <img src="assets/showcase/P2.png" width="100%" alt="RouteFilter 如何影响车辆寻路">
</p>

<!-- 截图占位：assets/screenshots/04-rerouting-result.png -->
<!-- ![受限车辆选择替代路线](assets/screenshots/04-rerouting-result.png) -->

## 安装

### Paradox Mods

在 [Paradox Mods](https://mods.paradoxplaza.com/mods/155839/Windows) 上订阅 **RouteFilter** 并将其加入当前播放集。

如果播放集提示需要重启，请重启游戏。

### 手动安装

1. 从 [GitHub Releases](https://github.com/Daotie/CS2-RouteFilter/releases) 下载最新压缩包。
2. 解压到《都市：天际线 II》本地模组目录。
3. 在当前播放集中启用 **RouteFilter**。
4. 替换旧 DLL 后请重启游戏。

> [!IMPORTANT]
> 更改代码模组组合前，请备份重要城市存档。

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

RouteFilter `1.0.5` 使用带版本号的存档数据块保存每个受限目标的禁行资产清单，以稳定的 prefab 名称存储，并在读档后自动重新应用。

`1.0.1` 及更早版本的存档仍可正常读取，并保留原有的逐实体限制数据。

重建、替换或删除道路、轨道路段会使游戏创建新实体，与原目标关联的限制可能因此丢失。大规模改造路网后请重新检查限制。

## 重要行为与已知限制

- 资产名称来自游戏或资产作者提供的技术名称，不一定存在本地化显示名。
- 仅当游戏明确提供固定挂车或动车组关系时，界面才会显示车头—车厢分组。
- 固定公共交通线路不一定存在可行绕行路线。没有替代路线时，受影响车辆会停车并可能重试；RouteFilter 不会自动重绘玩家创建的公交或轨道线路。
- 在寻路屏障生效的短暂窗口内，其他同时请求路径的车辆也可能避开所选目标。
- 无法保证兼容所有替换车辆导航、寻路或路网实体的模组。报告冲突时请使用最小播放集并附上日志。

## 社区

欢迎加入 RouteFilter 社区，提出问题、分享模组使用方式、关注开发进展、参与测试并讨论新的功能想法。

- **[Discord](https://discord.gg/Y9UXFCkqmD)** — 快速交流、使用帮助、案例展示、功能建议与测试反馈。
- **[Paradox Forum](https://forum.paradoxplaza.com/forum/threads/mod-routefilter-per-asset-access-control-for-roads-rails.1938927/)** — 长期讨论、一般反馈与社区支持。
- **[GitHub Issues](https://github.com/Daotie/CS2-RouteFilter/issues)** — 正式 Bug 报告、性能问题、兼容性问题与功能追踪。
- **[Paradox Mods](https://mods.paradoxplaza.com/mods/155839/Windows)** — 官方下载与更新渠道。

**欢迎使用中文或英文交流。**

### 应该使用哪个渠道？

| 渠道 | 适合的内容 |
| --- | --- |
| Discord | 快速提问、一般讨论、社区支持、案例展示、功能建议与测试反馈 |
| Paradox Forum | 长期讨论、一般反馈与社区支持 |
| GitHub Issues | 需要正式跟踪的可复现 Bug、性能问题、兼容性问题和功能建议 |
| Paradox Mods | 安装、订阅及更新 RouteFilter |

## 兼容性与支持

RouteFilter 使用《都市：天际线 II》官方代码模组工具链，无需额外的框架模组。

遇到问题时，请先使用最新版本的 RouteFilter，并在尽可能精简的播放集下尝试复现。

报告技术问题时，请根据实际情况尽可能提供：

- 游戏版本
- RouteFilter 版本
- 问题描述
- 预期行为
- 复现步骤
- 当前启用的交通或路网相关模组
- `Player.log`
- 播放集信息
- 相关截图或视频

一般使用问题和非正式反馈可以直接在 Discord 或 Paradox Forum 中交流。

对于能够稳定复现的 Bug、性能问题、兼容性问题，以及需要持续跟踪的功能建议，请尽可能使用 [GitHub Issues](https://github.com/Daotie/CS2-RouteFilter/issues)。

### 项目文档

- 使用帮助与 Bug 报告：[SUPPORT.md](SUPPORT.md)
- 安全问题披露：[SECURITY.md](SECURITY.md)
- 版本历史：[CHANGELOG.md](CHANGELOG.md)
- 贡献规范：[CONTRIBUTING.md](CONTRIBUTING.md)

## 开发

RouteFilter 是开源项目，欢迎参与贡献。

### 开发环境要求

- 《都市：天际线 II》及官方模组工具链
- .NET SDK
- Node.js 18 或更高版本

### 构建

```powershell
dotnet build RouteFilter.csproj -c Release

cd UI
npm ci

$env:ROUTEFILTER_OUTPUT_DIR = (Join-Path (Get-Location) "build")
npm run build
npm audit --omit=dev
```

官方 Paradox Mods 元数据和发布配置文件位于 `Properties`。

完整发布检查表请参阅 [RELEASING.md](RELEASING.md)。

## 许可证与版权

Copyright © 2026 Daotie。RouteFilter 原创源代码及其他原创项目材料的版权归其各自版权持有人所有。

RouteFilter 是自由及开源软件，采用 [GNU General Public License version 3 only](LICENSE)（`GPL-3.0-only`）许可。

你可以按照 GNU General Public License version 3 的条款使用、研究、修改和再分发 RouteFilter。对于受该许可证约束的内容，如进行修改并再分发，必须遵守 GPL-3.0 的相关要求，包括相应的源代码提供及许可证要求。

完整许可证条款请参阅仓库中的 [LICENSE](LICENSE) 文件。

除非另有明确说明，由 RouteFilter 项目提供的原创源代码均采用 `GPL-3.0-only` 许可。

第三方库、依赖项、资产、游戏资源、商标以及其他第三方材料不会因包含或引用于 RouteFilter 而被重新许可。这些内容仍分别受其各自许可证、使用条款、版权及其他知识产权约束。

除非另有明确约定或声明，提交至本仓库的贡献内容视为按照本项目适用的 `GPL-3.0-only` 许可证提供。

## 商标与项目归属

《都市：天际线 II》（Cities: Skylines II）、Colossal Order、Paradox Interactive 及相关名称、标志、商标、游戏资产和其他知识产权均属于其各自权利人。

RouteFilter 是由 Daotie 独立开发和维护的社区开源项目。

RouteFilter 与 Colossal Order 或 Paradox Interactive 不存在隶属、赞助、授权、认可、背书或其他官方合作关系。

本项目对《都市：天际线 II》及相关产品名称的引用仅用于说明兼容性及其目标游戏环境。
