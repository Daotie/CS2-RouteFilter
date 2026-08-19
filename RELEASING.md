# Release Process / 发布流程

## Stable and maintenance releases

1. Choose a Semantic Versioning release number and write player-facing English and Simplified Chinese notes.
2. Update `VERSION`, `Mod.cs`, `RouteFilter.csproj`, UI package metadata, Paradox Mods metadata, README badges, changelog, and release notes.
3. Confirm the asset-level save schema remains compatible or document a migration before release.
4. Build C# in Release configuration with zero warnings and errors.
5. Run `npm ci`, `npm run build`, and `npm audit --omit=dev` in `UI`.
6. Verify panel/shortcut synchronization, responsive layout, target selection, target-specific list loading, exact asset matching, road and rail enforcement, outside traffic, fixed-route behavior, restriction clearing, save/reload, and both supported UI languages.
7. Build the distribution archive and record its SHA-256 digest.
8. Create a signed Conventional Commit and signed version tag.
9. Publish the GitHub release with the matching archive and notes, then verify repository visibility and automated checks.
10. For Paradox Mods, provide the final thumbnail and screenshots, validate `Properties/PublishConfiguration.xml`, and use the appropriate profile under `Properties/PublishProfiles`.
11. After the first Paradox Mods submission, store the returned `ModId` in the publish configuration. Use `PublishNewVersion` for later releases and `UpdateCurrentVersion` only to correct metadata or files without creating a new public version.

The local official publisher targets .NET 6. If only a newer compatible .NET runtime is installed, set `$env:DOTNET_ROLL_FORWARD = "Major"` for the publishing process. Build the C# deploy output first, build the UI into the same local mod content folder second, then invoke the selected publish profile so both DLL and UI files are present.

## Prereleases

Use one new prerelease version for each externally tested correction. Do not replace an already published prerelease archive under the same version number. Mark GitHub prereleases clearly and keep historical notes in `CHANGELOG.md`.

## 中文要求

正式版与维护版必须统一版本号、双语公开说明、存档兼容策略、C#/UI/依赖检查、游戏内道路与轨道验证、发行包哈希、签名提交和标签。Paradox Mods 首次提交前必须准备最终缩略图与游戏内截图；首次提交取得 `ModId` 后，将其写回发布配置。对外测试修复应一次使用一个新的预发行版本号，不覆盖已经发布的同版本压缩包。
