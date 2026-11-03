// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace CMUmcl.Community
{
    internal static class CompatibilityEvaluator
    {
        private static readonly HashSet<string> KnownLoaders = new(StringComparer.OrdinalIgnoreCase) { "fabric", "forge", "neoforge", "quilt" };

        public static LoaderState LoaderStateFrom(string? loader)
        {
            string normalized = (loader ?? "").Trim().ToLowerInvariant();
            if (normalized == "vanilla") return LoaderState.Vanilla;
            if (KnownLoaders.Contains(normalized)) return LoaderState.Known;
            return LoaderState.Unknown;
        }

        /// <summary>One-way loader acceptance: a Quilt instance accepts Fabric; Fabric does not accept Quilt. Null/empty inputs are normalized to "".</summary>
        public static bool LoaderAccepts(string? instanceLoader, string? versionLoader)
        {
            string a = (instanceLoader ?? "").Trim().ToLowerInvariant();
            string b = (versionLoader ?? "").Trim().ToLowerInvariant();
            if (a == b) return true;
            return a == "quilt" && b == "fabric";
        }

        public static bool AnyLoaderMatches(string? instanceLoader, IReadOnlyList<string>? versionLoaders)
        {
            if (versionLoaders == null) return false;
            foreach (string l in versionLoaders)
                if (LoaderAccepts(instanceLoader, l)) return true;
            return false;
        }

        /// <summary>Project-level hint inferred from the search summary only (file-level attrs never participate).</summary>
        public static CompatibilityHint ProjectHint(ResourceProject project, ResourceKind kind, string? instanceLoader, string? gameVersion)
        {
            if(kind==ResourceKind.Modpack)return CompatibilityHint.Unknown;
            // A missing target game version cannot be compared against the source's game versions:
            // report Unknown, never Incompatible.
            if (string.IsNullOrWhiteSpace(gameVersion)) return CompatibilityHint.Unknown;

            bool hasLoaders = project.Loaders != null && project.Loaders.Count > 0;
            bool hasGames = project.GameVersions != null && project.GameVersions.Count > 0;

            if (kind == ResourceKind.Mod)
            {
                if (string.IsNullOrWhiteSpace(instanceLoader)) return CompatibilityHint.Unknown;
                if (!hasLoaders || !hasGames) return CompatibilityHint.Unknown;
                bool loaderOk = AnyLoaderMatches(instanceLoader, project.Loaders);
                bool gameOk = project.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase);
                if (!loaderOk || !gameOk) return CompatibilityHint.Incompatible;
                return CompatibilityHint.Compatible;
            }
            else // ResourcePack / Shader / Datapack: only GameVersions
            {
                if (!hasGames) return CompatibilityHint.Unknown;
                return project.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase)
                    ? CompatibilityHint.Compatible
                    : CompatibilityHint.Incompatible;
            }
        }

        /// <summary>Hard blocks (Chinese messages) and soft warnings for an install decision.</summary>
        public sealed class InstallDecision
        {
            public List<string> HardBlocks = new();
            public List<string> SoftWarnings = new();
        }

        public static InstallDecision EvaluateInstall(
            ResourceKind kind,
            LoaderState state,
            string instanceLoader,
            string instanceGame,
            IReadOnlyList<string> versionLoaders,
            IReadOnlyList<string> versionGameVersions,
            ResourceFile file,
            bool datapackWorldSelected)
        {
            var d = new InstallDecision();
            bool hasLoaders = versionLoaders != null && versionLoaders.Count > 0;
            bool hasGames = versionGameVersions != null && versionGameVersions.Count > 0;

            // Extension hard block.
            string ext = System.IO.Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
            bool extOk = kind switch
            {
                ResourceKind.Mod => ext == ".jar",
                ResourceKind.ResourcePack or ResourceKind.Shader or ResourceKind.Datapack => ext == ".zip",
                ResourceKind.Modpack => ext == ".mrpack",
                _ => false,
            };
            if (!extOk) d.HardBlocks.Add("文件类型与所选分类不符。");

            // Hash hard block (size alone never suffices).
            if (!file.HasAnyHash)
                d.HardBlocks.Add("来源未提供 SHA-1/SHA-512 哈希，无法校验，禁止安装。");

            if (kind == ResourceKind.Modpack)
            {
                // Modpack bypasses target-instance compatibility entirely.
                return d;
            }

            if (kind == ResourceKind.Mod)
            {
                if (state == LoaderState.Vanilla)
                    d.HardBlocks.Add("该实例是原版，未安装 Mod 加载器，无法安装 Mod。");
                else if (state == LoaderState.Unknown)
                    d.HardBlocks.Add("实例加载器无法判定（元数据缺失/损坏、加载器为空/空白或不受支持），无法安装 Mod，请修复 instance.json 后重试。");
                else if (state == LoaderState.Known && hasLoaders && !AnyLoaderMatches(instanceLoader, versionLoaders))
                    d.HardBlocks.Add("所选 Mod 的加载器与实例不兼容（" + instanceLoader + "），无法安装。");
            }

            if (kind == ResourceKind.Datapack && !datapackWorldSelected)
                d.HardBlocks.Add("该实例还没有世界。请先进入游戏创建/载入一个世界，再安装数据包。");

            if (kind == ResourceKind.Mod && !hasLoaders)
                d.SoftWarnings.Add("来源未声明兼容加载器，无法判断兼容性，仍要继续吗？");
            if (!hasGames)
                d.SoftWarnings.Add("来源未声明兼容版本/加载器，无法判断兼容性，仍要继续吗？");
            else if (!string.IsNullOrWhiteSpace(instanceGame) && !versionGameVersions.Contains(instanceGame, StringComparer.OrdinalIgnoreCase))
                d.SoftWarnings.Add("所选版本可能不兼容实例的 Minecraft 版本，仍要继续安装吗？");

            return d;
        }
    }
}
