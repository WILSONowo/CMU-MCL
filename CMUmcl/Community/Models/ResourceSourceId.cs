// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Community
{
    internal enum ResourceSourceId { Modrinth, CurseForge }

    internal static class ResourceSourceLabels
    {
        public static string ToDisplayName(ResourceSourceId source) => source switch
        {
            ResourceSourceId.Modrinth => "Modrinth",
            ResourceSourceId.CurseForge => "CurseForge",
            _ => source.ToString(),
        };

        /// <summary>Plan alias: human display label for a source.</summary>
        public static string Label(ResourceSourceId source) => ToDisplayName(source);
    }
}
