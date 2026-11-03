// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CMUmcl.Content;

namespace CMUmcl.Community
{
    internal sealed class ResourceTarget
    {
        public string Id = "";
        public string InstallPath = "";
        public string InstanceDir = "";
        public string Name { get; set; } = "";
        public string Game = "";
        public string Loader = "";
        public LoaderState LoaderState = LoaderState.Unknown;
        public string? GameDir;   // null when unresolvable (Unknown loader under ModdedOnly)
        public string? Error;     // "实例记录损坏，已跳过" when installPath invalid
    }

    /// <summary>
    /// Enumerates installable instances and resolves gameDir / target directories / worlds.
    /// Only used for Mod / ResourcePack / Shader / Datapack; Modpack bypasses this entirely.
    /// </summary>
    internal sealed class ResourceTargets
    {
        private readonly InstalledStateStore _installedState;
        public ResourceTargets(InstalledStateStore installedState) { _installedState = installedState; }

        public IReadOnlyList<ResourceTarget> EnumerateInstances()
        {
            var result = new List<ResourceTarget>();
            foreach (var kv in _installedState.ListRecords())
            {
                string id = kv.Key;
                string installPath = string.IsNullOrWhiteSpace(kv.Value.InstallPath) ? id : kv.Value.InstallPath!;

                string instanceDir;
                try
                {
                    instanceDir = PackFileSystem.ResolveUnder(LauncherPaths.InstancesDirectory, PackFileSystem.SanitizeRelativePath(installPath));
                }
                catch (ResourceException)
                {
                    result.Add(new ResourceTarget { Id = id, InstallPath = installPath, Error = "实例记录损坏，已跳过。" });
                    continue;
                }

                InstanceMetadata? meta = null;
                string metaPath = Path.Combine(instanceDir, "instance.json");
                if (File.Exists(metaPath))
                {
                    try { meta = JsonSerializer.Deserialize<InstanceMetadata>(File.ReadAllText(metaPath)); }
                    catch { meta = null; }
                }

                string displayName=kv.Value.DisplayName ?? meta?.Name ?? id;
                try{var options=InstanceOptionsStore.Load(instanceDir);if(!string.IsNullOrWhiteSpace(options.Name))displayName=options.Name!;}catch{ }
                string loader = meta?.Loader ?? "";
                LoaderState state = meta == null ? LoaderState.Unknown : CompatibilityEvaluator.LoaderStateFrom(loader);
                result.Add(new ResourceTarget
                {
                    Id = id,
                    InstallPath = installPath,
                    InstanceDir = instanceDir,
                    Name = displayName,
                    Game = meta?.Game ?? "",
                    Loader = loader,
                    LoaderState = state,
                    GameDir = ResolveGameDir(SettingsStore.Current, instanceDir, state, loader),
                });
            }
            return result;
        }

        /// <summary>Resolves gameDir for a target; Unknown loader under ModdedOnly → null (unresolvable).</summary>
        internal static string? ResolveGameDir(AppSettings settings, string instanceDir, LoaderState state, string loader)
        {
            switch (settings.VersionIsolation)
            {
                case VersionIsolation.All:
                    return instanceDir;
                case VersionIsolation.Off:
                    return LauncherPaths.GameDirectory;
                case VersionIsolation.ModdedOnly:
                    if (state == LoaderState.Known && GameDirectoryResolver.IsModdedLoader(loader)) return instanceDir;
                    if (state == LoaderState.Vanilla) return LauncherPaths.GameDirectory;
                    return null; // Unknown: cannot determine; installation is disabled (never treat as vanilla).
                default:
                    return LauncherPaths.GameDirectory;
            }
        }

        /// <summary>Returns the absolute target directory for a non-datapack kind under gameDir.</summary>
        public static string ResolveTargetDirectory(ResourceKind kind, string gameDir)
        {
            string? sub = ResourceKindMaps.TargetSubdirectory(kind);
            if (sub == null) throw new ResourceException("此资源类型没有目标目录。");
            return Path.Combine(gameDir, sub);
        }

        /// <summary>Enumerates world directory names (direct children of gameDir\saves containing level.dat).</summary>
        public IReadOnlyList<string> EnumerateWorlds(string gameDir)
        {
            string saves = Path.Combine(gameDir, "saves");
            if (!Directory.Exists(saves)) return Array.Empty<string>();
            var worlds = new List<string>();
            foreach (string dir in Directory.EnumerateDirectories(saves))
            {
                string name = Path.GetFileName(dir);
                try
                {
                    string resolved = PackFileSystem.ResolveUnder(saves, PackFileSystem.SanitizeRelativePath(name));
                    if (File.Exists(Path.Combine(resolved, "level.dat")))
                        worlds.Add(name);
                }
                catch (ResourceException) { /* invalid world name: skip */ }
            }
            return worlds;
        }

        /// <summary>Resolves the datapack target directory for a validated world name.</summary>
        public static string ResolveDatapackDirectory(string gameDir, string worldName)
        {
            string saves = Path.Combine(gameDir, "saves");
            string world = PackFileSystem.ResolveUnder(saves, PackFileSystem.SanitizeRelativePath(worldName));
            if (!string.Equals(Path.GetDirectoryName(world)?.TrimEnd('\\'), saves.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ResourceException("世界名称无效或越界，已拒绝。");
            return Path.Combine(world, "datapacks");
        }
    }
}
