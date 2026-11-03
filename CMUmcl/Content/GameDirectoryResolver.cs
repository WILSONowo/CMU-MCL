// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Content
{
    /// <summary>
    /// Shared game-directory resolution for launch and resource install. Extracted from the
    /// previous private LaunchService logic so both paths can never drift.
    /// </summary>
    internal static class GameDirectoryResolver
    {
        public static string ResolveGameDirectory(AppSettings settings, string instanceDir, string loader)
        {
            switch (settings.VersionIsolation)
            {
                case VersionIsolation.All:
                    return instanceDir;

                case VersionIsolation.ModdedOnly:
                    return IsModdedLoader(loader) ? instanceDir : LauncherPaths.GameDirectory;

                case VersionIsolation.Off:
                default:
                    return LauncherPaths.GameDirectory;
            }
        }

        public static bool IsModdedLoader(string? loader)
        {
            string normalized = (loader ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is not ("" or "vanilla");
        }
    }
}
