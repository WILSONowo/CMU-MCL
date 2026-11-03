using CMUmcl.Content;

internal static partial class Program
{
    private static void ResolverChecks()
    {
        string instanceDir = @"D:\instances\i1";
        Check(GameDirectoryResolver.ResolveGameDirectory(new AppSettings { VersionIsolation = VersionIsolation.All }, instanceDir, "vanilla") == instanceDir, "resolver: All+vanilla → instanceDir");
        Check(GameDirectoryResolver.ResolveGameDirectory(new AppSettings { VersionIsolation = VersionIsolation.All }, instanceDir, "fabric") == instanceDir, "resolver: All+modded → instanceDir");
        Check(GameDirectoryResolver.ResolveGameDirectory(new AppSettings { VersionIsolation = VersionIsolation.Off }, instanceDir, "vanilla") == LauncherPaths.GameDirectory, "resolver: Off+vanilla → shared");
        Check(GameDirectoryResolver.ResolveGameDirectory(new AppSettings { VersionIsolation = VersionIsolation.Off }, instanceDir, "fabric") == LauncherPaths.GameDirectory, "resolver: Off+modded → shared");
        Check(GameDirectoryResolver.ResolveGameDirectory(new AppSettings { VersionIsolation = VersionIsolation.ModdedOnly }, instanceDir, "fabric") == instanceDir, "resolver: ModdedOnly+fabric → instanceDir");
        Check(GameDirectoryResolver.ResolveGameDirectory(new AppSettings { VersionIsolation = VersionIsolation.ModdedOnly }, instanceDir, "vanilla") == LauncherPaths.GameDirectory, "resolver: ModdedOnly+vanilla → shared");
        Check(!GameDirectoryResolver.IsModdedLoader("vanilla") && !GameDirectoryResolver.IsModdedLoader("") && !GameDirectoryResolver.IsModdedLoader(null), "resolver: vanilla/empty/null are not modded");
        Check(GameDirectoryResolver.IsModdedLoader("Fabric") && GameDirectoryResolver.IsModdedLoader(" quilt "), "resolver: recognized loaders are modded (case/whitespace normalized)");
    }
}
