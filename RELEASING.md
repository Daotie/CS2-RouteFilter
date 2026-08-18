# Release Process / 发布流程

1. Confirm the target version follows Semantic Versioning prerelease conventions.
2. Update `VERSION`, `Mod.cs`, `RouteFilter.csproj`, `UI/mod.json`, `UI/package.json`, `UI/package-lock.json`, publish metadata, both README badges, changelog, and release notes.
3. Build C# in Release configuration with zero warnings and errors.
4. Run `npm ci`, `npm run build`, and `npm audit --omit=dev` in `UI`.
5. Test tool activation, asset-catalog discovery and search, exact prefab matching, node and segment restrictions, alternate-route requests, save/reload, restriction clearing, language switching, and behavior without an available detour.
6. Build the distribution archive and record its SHA-256 digest.
7. Create a signed Conventional Commit and signed version tag.
8. Publish a GitHub prerelease for beta versions. Keep the repository private until the first stable release passes gameplay and save-compatibility testing.
9. For the first stable release, confirm documentation and assets contain no beta warnings that are no longer applicable, then change repository visibility to public.

中文发布要求与上述步骤一致：版本统一、C#/UI/依赖检查、游戏内功能和存档测试、发行包哈希、签名提交与标签、Beta 预发行，以及正式版通过验证后再公开仓库。
