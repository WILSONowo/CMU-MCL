// SPDX-License-Identifier: GPL-3.0-only
using System;
using CMUmcl.Community;

internal static partial class Program
{
    private static void ModelChecks()
    {
        // Kind mappings (Modrinth facets)
        Check(ResourceKindMaps.ModrinthTypeFacet(ResourceKind.Mod) == "project_type:mod", "kind: Mod Modrinth facet");
        Check(ResourceKindMaps.ModrinthTypeFacet(ResourceKind.Modpack) == "project_type:modpack", "kind: Modpack Modrinth facet");
        Check(ResourceKindMaps.ModrinthTypeFacet(ResourceKind.Datapack) == "all_project_types:datapack", "kind: Datapack Modrinth facet");
        Check(ResourceKindMaps.ModrinthTypeFacet(ResourceKind.ResourcePack) == "project_type:resourcepack", "kind: ResourcePack Modrinth facet");
        Check(ResourceKindMaps.ModrinthTypeFacet(ResourceKind.Shader) == "project_type:shader", "kind: Shader Modrinth facet");

        // Plan alias: ModrinthFacet == ModrinthTypeFacet
        Check(ResourceKindMaps.ModrinthFacet(ResourceKind.Mod) == "project_type:mod", "kind: Mod ModrinthFacet alias");
        Check(ResourceKindMaps.ModrinthFacet(ResourceKind.Datapack) == "all_project_types:datapack", "kind: Datapack ModrinthFacet alias");

        Check(ResourceKindMaps.CurseForgeClassId(ResourceKind.Mod) == 6, "kind: Mod CurseForge classId");
        Check(ResourceKindMaps.CurseForgeClassId(ResourceKind.Modpack) == 4471, "kind: Modpack CurseForge classId");
        Check(ResourceKindMaps.CurseForgeClassId(ResourceKind.Datapack) == 6945, "kind: Datapack CurseForge classId");
        Check(ResourceKindMaps.CurseForgeClassId(ResourceKind.ResourcePack) == 12, "kind: ResourcePack CurseForge classId");
        Check(ResourceKindMaps.CurseForgeClassId(ResourceKind.Shader) == 6552, "kind: Shader CurseForge classId");

        Check(ResourceKindMaps.TargetSubdirectory(ResourceKind.Mod) == "mods", "kind: Mod target subdir");
        Check(ResourceKindMaps.TargetSubdirectory(ResourceKind.Modpack) == null, "kind: Modpack target subdir null");
        Check(ResourceKindMaps.TargetSubdirectory(ResourceKind.Datapack) == null, "kind: Datapack target subdir null");
        Check(ResourceKindMaps.TargetSubdirectory(ResourceKind.ResourcePack) == "resourcepacks", "kind: ResourcePack target subdir");
        Check(ResourceKindMaps.TargetSubdirectory(ResourceKind.Shader) == "shaderpacks", "kind: Shader target subdir");

        Check(ResourceKindMaps.DisplayName(ResourceKind.Mod) == "Mod", "kind: Mod display name");
        Check(ResourceKindMaps.DisplayName(ResourceKind.Modpack) == "整合包", "kind: Modpack display name");
        Check(ResourceKindMaps.DisplayName(ResourceKind.Datapack) == "数据包", "kind: Datapack display name");
        Check(ResourceKindMaps.DisplayName(ResourceKind.ResourcePack) == "资源包", "kind: ResourcePack display name");
        Check(ResourceKindMaps.DisplayName(ResourceKind.Shader) == "光影包", "kind: Shader display name");

        // Plan alias: DisplayLabel == DisplayName
        Check(ResourceKindMaps.DisplayLabel(ResourceKind.Mod) == "Mod", "kind: Mod DisplayLabel alias");
        Check(ResourceKindMaps.DisplayLabel(ResourceKind.Modpack) == "整合包", "kind: Modpack DisplayLabel alias");

        // Query page-size clamp (Task 9)
        Check(new ResourceQuery { PageSize = 0 }.ClampedPageSize == 1, "query: 0 clamps to 1");
        Check(new ResourceQuery { PageSize = 500 }.ClampedPageSize == 50, "query: 500 clamps to 50");
        Check(ResourceQuery.ClampPageSize(25) == 25, "query: 25 unchanged");

        // Query normalization
        Check(new ResourceQuery { Index = -5 }.Normalize().Index == 0, "query: negative Index -> 0");
        Check(new ResourceQuery { PageSize = 0 }.Normalize().PageSize == 1, "query: PageSize 0 -> 1");
        Check(new ResourceQuery { PageSize = 100 }.Normalize().PageSize == 50, "query: PageSize 100 -> 50");
        Check(new ResourceQuery { PageSize = 20 }.Normalize().PageSize == 20, "query: PageSize 20 unchanged");
        Check(new ResourceQuery { SearchText = "  " }.Normalize().SearchText == null, "query: whitespace SearchText -> null");
        Check(new ResourceQuery { GameVersion = "   " }.Normalize().GameVersion == null, "query: whitespace GameVersion -> null");
        Check(new ResourceQuery { Loader = "FABRIC" }.Normalize().Loader == "fabric", "query: Loader uppercase -> lower");
        Check(new ResourceQuery { Loader = "  Forge  " }.Normalize().Loader == "forge", "query: Loader trimmed + lower");
        Check(new ResourceQuery { SearchText = "  hello  " }.Normalize().SearchText == "hello", "query: SearchText trimmed");

        var q = new ResourceQuery { SearchText = "x", Kind = ResourceKind.Shader, Source = ResourceSourceId.Modrinth, Sort = ResourceSort.Downloads, Index = 3, PageSize = 20 };
        var n = q.Normalize();
        Check(n.Kind == ResourceKind.Shader && n.Source == ResourceSourceId.Modrinth && n.Sort == ResourceSort.Downloads && n.Index == 3, "query: fields preserved through Normalize");

        // ResourceFile.ToExpectation + hash presence
        var f1 = new ResourceFile { FileName = "a.jar", Size = 10, Sha1 = "abc" };
        var e1 = f1.ToExpectation();
        Check(e1.Size == 10 && e1.Sha1 == "abc" && e1.Sha512 == null && e1.Sha256 == null, "file: size + SHA-1 expectation");
        Check(f1.HasSecureHash, "file: SHA-1 present -> HasSecureHash true");

        var f2 = new ResourceFile { FileName = "b.jar", Size = 20, Sha512 = "def" };
        var e2 = f2.ToExpectation();
        Check(e2.Size == 20 && e2.Sha512 == "def" && e2.Sha1 == null, "file: size + SHA-512 expectation");
        Check(f2.HasSecureHash, "file: SHA-512 present -> HasSecureHash true");

        var f3 = new ResourceFile { FileName = "c.jar", Size = 30 };
        Check(!f3.ToExpectation().HasHash, "file: no hash -> HasHash false");
        Check(!f3.HasSecureHash, "file: no hash -> HasSecureHash false");
        Check(f3.ToExpectation().Size == 30, "file: size preserved without hash");

        var f4 = new ResourceFile { FileName = "d.jar", Size = 0, Sha1 = "x" };
        Check(f4.ToExpectation().Size == null, "file: size 0 -> null expectation size");
        Check(f4.HasSecureHash, "file: SHA-1 only -> HasSecureHash true");

        Check(new ResourceFile { Sha512 = "y" }.HasSecureHash, "file: SHA-512 only -> HasSecureHash true");

        // Plan alias: HasAnyHash == HasSecureHash (Task 9)
        Check(!new ResourceFile { FileName = "b.jar", Size = 5 }.HasAnyHash, "file: no hash flags HasAnyHash false");
        Check(new ResourceFile { Sha1 = "x" }.HasAnyHash, "file: sha1 counts as hash");
        Check(new ResourceFile { Sha512 = "y" }.HasAnyHash, "file: sha512 counts as hash");

        // Project label
        Check(new ResourceProject { Source = ResourceSourceId.Modrinth }.SourceLabel == "Modrinth", "project: Modrinth SourceLabel");
        Check(new ResourceProject { Source = ResourceSourceId.CurseForge }.SourceLabel == "CurseForge", "project: CurseForge SourceLabel");
        Check(new ResourceProject().Downloads == -1, "project: Downloads default -1 preserved");
        Check(new ResourceProject { Downloads = -1 }.Downloads == -1, "project: Downloads -1 preserved");

        // Plan alias: ResourceSourceLabels.Label == ToDisplayName
        Check(ResourceSourceLabels.Label(ResourceSourceId.Modrinth) == "Modrinth", "project: Modrinth Label alias");
        Check(ResourceSourceLabels.Label(ResourceSourceId.CurseForge) == "CurseForge", "project: CurseForge Label alias");

        // Enum members
        Check(Enum.IsDefined(typeof(LoaderState), LoaderState.Vanilla), "enum: LoaderState.Vanilla defined");
        Check(Enum.IsDefined(typeof(LoaderState), LoaderState.Known), "enum: LoaderState.Known defined");
        Check(Enum.IsDefined(typeof(LoaderState), LoaderState.Unknown), "enum: LoaderState.Unknown defined");
        Check(Enum.GetNames(typeof(LoaderState)).Length == 3, "enum: LoaderState has 3 members");

        Check(Enum.IsDefined(typeof(CompatibilityHint), CompatibilityHint.Compatible), "enum: CompatibilityHint.Compatible defined");
        Check(Enum.IsDefined(typeof(CompatibilityHint), CompatibilityHint.Incompatible), "enum: CompatibilityHint.Incompatible defined");
        Check(Enum.IsDefined(typeof(CompatibilityHint), CompatibilityHint.Unknown), "enum: CompatibilityHint.Unknown defined");
        Check(Enum.GetNames(typeof(CompatibilityHint)).Length == 3, "enum: CompatibilityHint has 3 members");

        Check(Enum.GetNames(typeof(ResourceSourceId)).Length == 2, "enum: ResourceSourceId has 2 members");
        Check(Enum.GetNames(typeof(ResourceKind)).Length == 5, "enum: ResourceKind has 5 members");
        Check(Enum.GetNames(typeof(ResourceSort)).Length == 4, "enum: ResourceSort has 4 members");
    }
}
