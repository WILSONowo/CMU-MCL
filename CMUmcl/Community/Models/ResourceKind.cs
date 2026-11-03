// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Community
{
    internal enum ResourceKind { Mod, Modpack, Datapack, ResourcePack, Shader }

    /// <summary>Central mapping for <see cref="ResourceKind"/> so source adapters share one place.</summary>
    internal static class ResourceKindMaps
    {
        public static string ModrinthTypeFacet(ResourceKind kind) => kind switch
        {
            ResourceKind.Mod => "project_type:mod",
            ResourceKind.Modpack => "project_type:modpack",
            ResourceKind.Datapack => "all_project_types:datapack",
            ResourceKind.ResourcePack => "project_type:resourcepack",
            ResourceKind.Shader => "project_type:shader",
            _ => "project_type:mod",
        };

        public static int CurseForgeClassId(ResourceKind kind) => kind switch
        {
            ResourceKind.Mod => 6,
            ResourceKind.Modpack => 4471,
            ResourceKind.ResourcePack => 12,
            ResourceKind.Shader => 6552,
            ResourceKind.Datapack => 6945,
            _ => 6,
        };

        /// <summary>Target subdirectory relative to gameDir. Null for Datapack (saves\world\datapacks) and Modpack (no target dir).</summary>
        public static string? TargetSubdirectory(ResourceKind kind) => kind switch
        {
            ResourceKind.Mod => "mods",
            ResourceKind.ResourcePack => "resourcepacks",
            ResourceKind.Shader => "shaderpacks",
            _ => null,
        };

        public static string DisplayName(ResourceKind kind) => kind switch
        {
            ResourceKind.Mod => "Mod",
            ResourceKind.Modpack => "整合包",
            ResourceKind.Datapack => "数据包",
            ResourceKind.ResourcePack => "资源包",
            ResourceKind.Shader => "光影包",
            _ => kind.ToString(),
        };

        /// <summary>Plan alias: Modrinth project-type facet for a kind.</summary>
        public static string ModrinthFacet(ResourceKind kind) => ModrinthTypeFacet(kind);

        /// <summary>Plan alias: human display label for a kind.</summary>
        public static string DisplayLabel(ResourceKind kind) => DisplayName(kind);
    }
}
