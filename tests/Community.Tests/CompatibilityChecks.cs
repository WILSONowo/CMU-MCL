// SPDX-License-Identifier: GPL-3.0-only
using CMUmcl.Community;

internal static partial class Program
{
    private static ResourceProject Project(string[] loaders, string[] games) =>
        new ResourceProject { Loaders = loaders, GameVersions = games };

    private static void CompatibilityChecks()
    {
        var p = Project(new[] { "fabric" }, new[] { "1.20.1" });
        Check(CompatibilityEvaluator.ProjectHint(p, ResourceKind.Mod, "fabric", "1.20.1") == CompatibilityHint.Compatible, "hint: mod both dims match → Compatible");
        Check(CompatibilityEvaluator.ProjectHint(p, ResourceKind.Mod, "forge", "1.20.1") == CompatibilityHint.Incompatible, "hint: mod loader mismatch → Incompatible");
        Check(CompatibilityEvaluator.ProjectHint(p, ResourceKind.Mod, "fabric", "1.19.2") == CompatibilityHint.Incompatible, "hint: mod game mismatch → Incompatible");
        Check(CompatibilityEvaluator.ProjectHint(new ResourceProject { Loaders = Array.Empty<string>(), GameVersions = new[] { "1.20.1" } }, ResourceKind.Mod, "fabric", "1.20.1") == CompatibilityHint.Unknown, "hint: mod missing loaders → Unknown");
        Check(CompatibilityEvaluator.ProjectHint(new ResourceProject { Loaders = new[] { "fabric" }, GameVersions = Array.Empty<string>() }, ResourceKind.Mod, "fabric", "1.20.1") == CompatibilityHint.Unknown, "hint: mod missing games → Unknown");
        Check(CompatibilityEvaluator.ProjectHint(new ResourceProject { Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } }, ResourceKind.Mod, "quilt", "1.20.1") == CompatibilityHint.Compatible, "hint: quilt instance accepts fabric (one-way)");
        Check(CompatibilityEvaluator.ProjectHint(new ResourceProject { Loaders = new[] { "quilt" }, GameVersions = new[] { "1.20.1" } }, ResourceKind.Mod, "fabric", "1.20.1") == CompatibilityHint.Incompatible, "hint: fabric instance rejects quilt (one-way)");
        Check(CompatibilityEvaluator.ProjectHint(new ResourceProject { Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } }, ResourceKind.Mod, "fabric", "1.20.1") != CompatibilityHint.Unknown, "hint: Compatible never equals Unknown");
        Check(CompatibilityEvaluator.ProjectHint(Project(new[] { "fabric" }, new[] { "1.20.1" }), ResourceKind.Mod, "fabric", null) == CompatibilityHint.Unknown, "hint: mod null target game version → Unknown");
        Check(CompatibilityEvaluator.ProjectHint(Project(new[] { "fabric" }, new[] { "1.20.1" }), ResourceKind.Mod, "fabric", "") == CompatibilityHint.Unknown, "hint: mod empty target game version → Unknown");

        Check(CompatibilityEvaluator.ProjectHint(Project(Array.Empty<string>(), new[] { "1.20.1" }), ResourceKind.ResourcePack, "fabric", "1.20.1") == CompatibilityHint.Compatible, "hint: resourcepack game match → Compatible");
        Check(CompatibilityEvaluator.ProjectHint(Project(Array.Empty<string>(), new[] { "1.19.2" }), ResourceKind.ResourcePack, "fabric", "1.20.1") == CompatibilityHint.Incompatible, "hint: resourcepack game mismatch → Incompatible");
        Check(CompatibilityEvaluator.ProjectHint(Project(Array.Empty<string>(), new[] { "1.20.1" }), ResourceKind.ResourcePack, "fabric", null) == CompatibilityHint.Unknown, "hint: resourcepack null target game version → Unknown");
        Check(CompatibilityEvaluator.ProjectHint(Project(Array.Empty<string>(), Array.Empty<string>()), ResourceKind.Shader, "fabric", "1.20.1") == CompatibilityHint.Unknown, "hint: shader no games → Unknown");

        var jar = new ResourceFile { FileName = "x.jar", Sha1 = "a", Size = 1 };
        var zip = new ResourceFile { FileName = "x.zip", Sha512 = "b", Size = 1 };
        var badExt = new ResourceFile { FileName = "x.exe", Sha1 = "a", Size = 1 };
        var noHash = new ResourceFile { FileName = "x.jar", Size = 1 };

        var modVanilla = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Mod, LoaderState.Vanilla, "vanilla", "1.20.1", new[] { "fabric" }, new[] { "1.20.1" }, jar, false);
        Check(modVanilla.HardBlocks.Any(s => s.Contains("原版")), "install: mod into vanilla hard-blocked");

        var modUnknown = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Mod, LoaderState.Unknown, "", "1.20.1", new[] { "fabric" }, new[] { "1.20.1" }, jar, false);
        Check(modUnknown.HardBlocks.Any(s => s.Contains("无法判定")), "install: mod into unknown hard-blocked");

        var modMismatch = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Mod, LoaderState.Known, "forge", "1.20.1", new[] { "fabric" }, new[] { "1.20.1" }, jar, false);
        Check(modMismatch.HardBlocks.Any(s => s.Contains("不兼容")), "install: mod loader mismatch hard-blocked");

        var modQuiltOk = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Mod, LoaderState.Known, "quilt", "1.20.1", new[] { "fabric" }, new[] { "1.20.1" }, jar, false);
        Check(!modQuiltOk.HardBlocks.Any(s => s.Contains("不兼容")), "install: quilt instance accepts fabric");

        var datapackNoWorld = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Datapack, LoaderState.Known, "fabric", "1.20.1", Array.Empty<string>(), new[] { "1.20.1" }, zip, false);
        Check(datapackNoWorld.HardBlocks.Any(s => s.Contains("世界")), "install: datapack without world hard-blocked");

        var extBlock = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Mod, LoaderState.Known, "fabric", "1.20.1", new[] { "fabric" }, new[] { "1.20.1" }, badExt, true);
        Check(extBlock.HardBlocks.Any(s => s.Contains("文件类型")), "install: wrong extension hard-blocked");

        var hashBlock = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Mod, LoaderState.Known, "fabric", "1.20.1", new[] { "fabric" }, new[] { "1.20.1" }, noHash, true);
        Check(hashBlock.HardBlocks.Any(s => s.Contains("哈希")), "install: no SHA-1/SHA-512 hard-blocked (size alone insufficient)");

        var mrpack = new ResourceFile { FileName = "x.mrpack", Sha1 = "a", Size = 1 };
        var modpack = CompatibilityEvaluator.EvaluateInstall(ResourceKind.Modpack, LoaderState.Unknown, "", "", Array.Empty<string>(), Array.Empty<string>(), mrpack, false);
        Check(modpack.HardBlocks.Count == 0, "install: modpack bypasses target-instance rules");

        var oldVersion = CompatibilityEvaluator.EvaluateInstall(ResourceKind.ResourcePack, LoaderState.Known, "fabric", "1.20.1", Array.Empty<string>(), new[] { "1.19.2" }, zip, true);
        Check(oldVersion.HardBlocks.Count == 0 && oldVersion.SoftWarnings.Count > 0, "install: version mismatch is soft warning only");
    }
}
