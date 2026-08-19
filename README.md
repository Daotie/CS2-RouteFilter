<p align="center">
  <strong>English</strong> · <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <!-- Replaceable release logo slot: assets/branding/routefilter-logo.png -->
  <img src="assets/branding/routefilter-logo.png" width="240" alt="RouteFilter logo">
</p>

# RouteFilter

Exact vehicle-asset access control for road and rail networks in **Cities: Skylines II**.

![Version](https://img.shields.io/badge/version-1.0.0-2d8b70)
![Status](https://img.shields.io/badge/status-stable-1976d2)
![License](https://img.shields.io/badge/license-GPL--3.0--only-blue)

RouteFilter lets you decide which exact vehicle assets may pass through one network node or an entire road, tram, train, or subway segment. A matching vehicle is stopped before crossing the restricted target and is asked to find another route when the network provides one.

## Highlights

- Restrict individual vehicle assets instead of broad traffic categories.
- Apply independent forbidden lists to specific nodes or complete network segments.
- Support road vehicles, outside traffic, trams, trains, subway vehicles, engines, carriages, and recognized trailers.
- Discover available base-game, content-pack, and custom vehicle prefabs automatically.
- Filter the catalog to assets compatible with the selected road or rail target.
- Search, paginate, and scroll the asset catalog without hiding the application controls.
- Inspect an asset's maximum speed in km/h, acceleration, and braking on hover.
- Group recognized engines or tractors with their carriages and trailers while retaining individual control.
- Show clear node circles and segment outlines before selection, with a distinct selected-target highlight.
- Load the saved forbidden list belonging to the selected target; pending changes never silently affect the whole map.
- Request a detour through the game's pathfinding data and prevent a matching vehicle from simply driving through when no alternative exists.
- Store restrictions in the savegame and follow the game's English or Simplified Chinese language automatically.

## In-game overview

### 1. Select a precise network target

Choose **Node** or **Segment**, then left-click the highlighted target. Right-click cancels the selection. The panel opens together with the tool—there is no separate activation step.

<!-- Screenshot placeholder: assets/screenshots/01-target-selection.png -->
<!-- ![Selecting a highlighted road or rail target](assets/screenshots/01-target-selection.png) -->

### 2. Choose exact vehicle assets

Selected entries are the assets that will be **forbidden**. Road targets show road vehicles; rail targets show compatible rail assets. Hover an entry to inspect its base parameters. Expanding a recognized consist enables separate engine, carriage, or trailer choices.

<!-- Screenshot placeholder: assets/screenshots/02-asset-catalog.png -->
<!-- ![Filtering and selecting exact vehicle assets](assets/screenshots/02-asset-catalog.png) -->

### 3. Review and apply

**Forbid all assets** and **Allow all assets** only edit the pending list. Nothing is written to the map until **Apply list to selected target** is pressed. Selecting another target loads that target's own saved list. **Clear target restrictions** removes RouteFilter data from the selected target.

<!-- Screenshot placeholder: assets/screenshots/03-apply-restriction.png -->
<!-- ![Applying a forbidden list to one selected target](assets/screenshots/03-apply-restriction.png) -->

### 4. Vehicle enforcement and rerouting

RouteFilter checks current lanes, upcoming navigation lanes, path elements, node endpoints, and every recognized prefab in a vehicle consist. For a matching vehicle, it briefly marks the target unavailable to the pathfinder and invalidates that vehicle's current path. If a valid alternative exists, the vehicle can reroute; otherwise it is prevented from continuing normally through the restricted target and may retry.

<!-- Screenshot placeholder: assets/screenshots/04-rerouting-result.png -->
<!-- ![A restricted vehicle taking an alternate route](assets/screenshots/04-rerouting-result.png) -->

## Installation

### Paradox Mods

Subscribe to **RouteFilter** on Paradox Mods and add it to the active playset once the listing is available. Restart the game if the playset requests it.

### Manual installation

1. Download the latest archive from [GitHub Releases](https://github.com/Daotie/CS2-RouteFilter/releases).
2. Extract it into the local Cities: Skylines II mods directory.
3. Enable **RouteFilter** in the active playset.
4. Restart the game before replacing an older DLL.

Back up important cities before changing any code-mod setup.

## Quick start

1. Click the RouteFilter button in the top-left toolbar, or press `Ctrl+Shift+N`.
2. Choose **Node** or **Segment**.
3. Left-click the network target to edit.
4. Select the vehicle assets that should be forbidden.
5. Press **Apply list to selected target**.
6. Re-select the target at any time to review or change its saved list.

The shortcut is remappable in the game's settings. Closing the panel also closes the selection tool.

## Configuration

| Setting | Default | Purpose |
| --- | --- | --- |
| Asset-level restrictions | On | Enables exact prefab matching at restricted nodes and segments. |
| Emergency vehicle protection | Off | Allows police cars, ambulances, and fire engines even if their assets are selected. |
| Look-ahead lanes | 3 | Controls how early an approaching vehicle checks for a restriction. |
| Show/hide panel | `Ctrl+Shift+N` | Opens or closes both the RouteFilter panel and selection tool. |

## Save data and upgrades

RouteFilter `1.0.0` uses the asset-level V1 save schema introduced during the public beta. Each restricted target stores exact prefab entity references. Saves created with `0.4.0-beta.1` or `0.5.0-beta.1` remain on the same schema.

Rebuilding, replacing, or deleting a road or track segment creates new game entities and may remove restrictions attached to the original target. Review restrictions after substantial network reconstruction.

## Important behavior and limitations

- Asset names are technical prefab names supplied by the game or asset author; a localized display name may not exist.
- Engine/carriage grouping appears only where the game exposes a fixed-trailer or multiple-unit relationship.
- A fixed public-transport route cannot always be changed into a valid detour. When no alternative exists, the affected vehicle is stopped and may retry; RouteFilter does not redraw the player's transport line.
- During the short pathfinding barrier window, another vehicle requesting a route may also avoid the selected target.
- Compatibility with mods that replace vehicle navigation, pathfinding, or network entities cannot be guaranteed. Report conflicts with a minimal playset and logs.

## Compatibility and support

RouteFilter uses the official Cities: Skylines II code-mod toolchain and does not require a separate framework mod. For problems, first reproduce with the latest release and the smallest practical playset, then include the game version, RouteFilter version, reproduction steps, active traffic/network mods, and `Player.log`.

- Help and bug reports: [SUPPORT.md](SUPPORT.md)
- Security disclosures: [SECURITY.md](SECURITY.md)
- Release history: [CHANGELOG.md](CHANGELOG.md)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)

## Development

Requirements: Cities: Skylines II with the official modding toolchain, .NET SDK, and Node.js 18 or later.

```powershell
dotnet build RouteFilter.csproj -c Release
cd UI
npm ci
$env:ROUTEFILTER_OUTPUT_DIR = (Join-Path (Get-Location) "build")
npm run build
npm audit --omit=dev
```

The official Paradox Mods metadata and publish profiles are stored under `Properties`. See [RELEASING.md](RELEASING.md) for the complete release checklist.

## License and attribution

Copyright © 2026 Daotie. RouteFilter is licensed under the [GNU General Public License v3.0 only](LICENSE).

Cities: Skylines II and related names are trademarks of their respective owners. RouteFilter is an independent community project and is not affiliated with or endorsed by Colossal Order or Paradox Interactive.
