<p align="center">
  <strong>English</strong> · <a href="README.zh-CN.md">简体中文</a>
</p>

# RouteFilter

Asset-level vehicle access restrictions for road and rail nodes or segments in **Cities: Skylines II**.

![Version](https://img.shields.io/badge/version-0.4.0--beta.1-2d8b70)
![Status](https://img.shields.io/badge/status-beta-f0a202)
![License](https://img.shields.io/badge/license-GPL--3.0--only-blue)

Current beta: `0.4.0-beta.1`

RouteFilter adds a focused in-game tool for controlling which exact vehicle assets may use a selected network node or an entire road or rail segment. When a matching vehicle detects the restriction ahead, the mod promptly requests another route.

> RouteFilter is beta software. Back up important saves and review the known limitations before testing.

## Features

- Restrict individual vehicle assets rather than broad traffic classes.
- Discover base-game, content-pack, and available custom vehicle prefabs automatically.
- Search the asset list by prefab name and select any combination.
- Apply restrictions to one node or a complete road, tram, train, or subway segment.
- Request an alternate route as soon as a matching vehicle detects the restriction.
- Use a visible top-left panel; the keyboard shortcut remains optional.
- Follow the game's English or Simplified Chinese language setting automatically.
- Store the selected prefab references with each restricted target in the savegame.
- Configure emergency-vehicle exemptions and route look-ahead distance.

## Installation

1. Download the latest package from [GitHub Releases](https://github.com/Daotie/CS2-RouteFilter/releases).
2. Extract the package into the local Cities: Skylines II mods directory.
3. Enable **RouteFilter** in the active playset and restart the game when prompted.
4. Test with a backed-up save first.

## Usage

1. Open **RouteFilter** from the top-left game toolbar.
2. Choose **Node** or **Segment**.
3. Search for and select one or more vehicle assets.
4. Left-click a target to replace its restriction list with the current selection.
5. Right-click a target to clear its RouteFilter restriction.

The panel can refresh its asset catalog after content changes. `Ctrl+Shift+X` is available as an optional tool toggle.

## Configuration

| Setting | Default | Purpose |
| --- | --- | --- |
| Asset-level restrictions | On | Enforces exact prefab matches at nodes and segments. |
| Emergency vehicle protection | Off | Exempts police, ambulance, and fire vehicles even when selected. |
| Look-ahead lanes | 3 | Controls how early an approaching vehicle checks for restrictions. |

## Rerouting behavior

For a matching vehicle, RouteFilter briefly presents the selected node or segment as unavailable to the pathfinder, invalidates that vehicle's current path, and removes the temporary barrier after the route request finishes. Concurrent requests are reference-counted.

This prompts the affected vehicle to seek a detour, but it cannot guarantee that a valid alternate route exists or that the game will complete pathfinding within a fixed time.

## Save data

Version `0.4.0-beta.1` uses RouteFilter's asset-level V1 save schema. Each restricted target stores prefab entity references for the selected vehicle assets. Start testing this beta on a new city or a clearly identified backup branch of an existing save.

## Known limitations

- Prefab names are the technical asset names supplied by the game or asset author; localized display names are not always available.
- A different vehicle requesting a path during the short temporary-barrier window may also avoid the target.
- If no valid alternative exists, the affected vehicle may retry until access becomes possible or the restriction is removed.
- Rebuilding or replacing a network segment may remove the restriction associated with the original entity.
- Runtime behavior, save/reload, performance, and mod compatibility still require broader in-game testing before a stable release.

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

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution standards and [RELEASING.md](RELEASING.md) for the release checklist.

## Support and security

- Usage help and bug reports: [SUPPORT.md](SUPPORT.md)
- Security disclosures: [SECURITY.md](SECURITY.md)
- Release history: [CHANGELOG.md](CHANGELOG.md)

## License

Copyright © 2026 Daotie. RouteFilter is licensed under the [GNU General Public License v3.0 only](LICENSE).

Cities: Skylines II and related names are trademarks of their respective owners. RouteFilter is an independent community project and is not affiliated with or endorsed by Colossal Order or Paradox Interactive.
