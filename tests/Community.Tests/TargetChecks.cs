// SPDX-License-Identifier: GPL-3.0-only
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    private static void TargetChecks()
    {
        var all = new AppSettings { VersionIsolation = VersionIsolation.All };
        var off = new AppSettings { VersionIsolation = VersionIsolation.Off };
        var mod = new AppSettings { VersionIsolation = VersionIsolation.ModdedOnly };
        string dir = @"D:\instances\i1";
        Check(ResourceTargets.ResolveGameDir(all, dir, LoaderState.Vanilla, "vanilla") == dir, "target: All → instanceDir");
        Check(ResourceTargets.ResolveGameDir(off, dir, LoaderState.Known, "fabric") == LauncherPaths.GameDirectory, "target: Off → shared");
        Check(ResourceTargets.ResolveGameDir(mod, dir, LoaderState.Known, "fabric") == dir, "target: ModdedOnly+Known → instanceDir");
        Check(ResourceTargets.ResolveGameDir(mod, dir, LoaderState.Vanilla, "vanilla") == LauncherPaths.GameDirectory, "target: ModdedOnly+Vanilla → shared");
        Check(ResourceTargets.ResolveGameDir(mod, dir, LoaderState.Unknown, "") == null, "target: ModdedOnly+Unknown → null (unresolvable)");
        Check(ResourceTargets.ResolveGameDir(all, dir, LoaderState.Unknown, "") == dir, "target: All+Unknown → instanceDir");
        Check(ResourceTargets.ResolveGameDir(off, dir, LoaderState.Unknown, "") == LauncherPaths.GameDirectory, "target: Off+Unknown → shared");

        string gameDir = LauncherPaths.GameDirectory;
        Check(ResourceTargets.ResolveTargetDirectory(ResourceKind.Mod, gameDir).EndsWith("mods", StringComparison.Ordinal), "target: Mod → gameDir\\mods");
        Check(ResourceTargets.ResolveTargetDirectory(ResourceKind.ResourcePack, gameDir).EndsWith("resourcepacks", StringComparison.Ordinal), "target: ResourcePack → gameDir\\resourcepacks");
        Check(ResourceTargets.ResolveTargetDirectory(ResourceKind.Shader, gameDir).EndsWith("shaderpacks", StringComparison.Ordinal), "target: Shader → gameDir\\shaderpacks");

        bool modpackThrew = false;
        try { ResourceTargets.ResolveTargetDirectory(ResourceKind.Modpack, gameDir); }
        catch (ResourceException) { modpackThrew = true; }
        Check(modpackThrew, "target: Modpack has no target subdirectory");

        bool datapackThrew = false;
        try { ResourceTargets.ResolveTargetDirectory(ResourceKind.Datapack, gameDir); }
        catch (ResourceException) { datapackThrew = true; }
        Check(datapackThrew, "target: Datapack has no target subdirectory");

        var root = Path.Combine(Path.GetTempPath(), "CMUmcl-targets-" + Guid.NewGuid().ToString("N"));
        LauncherPaths.RootOverride = root;
        LauncherPaths.EnsureDirectories();
        var store = new InstalledStateStore();
        string instances = LauncherPaths.InstancesDirectory;

        string vDir = Path.Combine(instances, "vanilla1");
        Directory.CreateDirectory(vDir);
        File.WriteAllText(Path.Combine(vDir, "instance.json"), "{\"id\":\"local:a\",\"name\":\"Vanilla\",\"game\":\"1.20.1\",\"loader\":\"vanilla\"}");
        store.RecordInstall("local:a", 1, "vanilla1", "Vanilla");

        string fDir = Path.Combine(instances, "fabric1");
        Directory.CreateDirectory(fDir);
        File.WriteAllText(Path.Combine(fDir, "instance.json"), "{\"id\":\"local:b\",\"name\":\"Fabric\",\"game\":\"1.20.1\",\"loader\":\"fabric\"}");
        store.RecordInstall("local:b", 1, "fabric1", "Fabric");

        string eDir = Path.Combine(instances, "empty1");
        Directory.CreateDirectory(eDir);
        File.WriteAllText(Path.Combine(eDir, "instance.json"), "{\"id\":\"local:c\",\"name\":\"Empty\",\"game\":\"1.20.1\",\"loader\":\"\"}");
        store.RecordInstall("local:c", 1, "empty1", "Empty");

        string mDir = Path.Combine(instances, "missing1");
        Directory.CreateDirectory(mDir);
        store.RecordInstall("local:d", 1, "missing1", "Missing");

        store.RecordInstall("local:e", 1, "..\\escape", "Broken");

        var targets = new ResourceTargets(store);
        var list = targets.EnumerateInstances();
        ResourceTarget vanilla = list.First(t => t.Id == "local:a");
        ResourceTarget fabric = list.First(t => t.Id == "local:b");
        ResourceTarget empty = list.First(t => t.Id == "local:c");
        ResourceTarget missing = list.First(t => t.Id == "local:d");
        ResourceTarget broken = list.First(t => t.Id == "local:e");

        Check(vanilla.LoaderState == LoaderState.Vanilla, "target: explicit vanilla → Vanilla");
        Check(fabric.LoaderState == LoaderState.Known, "target: fabric → Known");
        Check(empty.LoaderState == LoaderState.Unknown, "target: empty loader → Unknown (never vanilla)");
        Check(missing.LoaderState == LoaderState.Unknown, "target: missing instance.json → Unknown");
        Check(broken.Error != null && broken.InstanceDir == "", "target: broken installPath skipped");

        string saves = Path.Combine(LauncherPaths.GameDirectory, "saves");
        Directory.CreateDirectory(Path.Combine(saves, "world1"));
        File.WriteAllText(Path.Combine(saves, "world1", "level.dat"), "x");
        Directory.CreateDirectory(Path.Combine(saves, "emptyworld"));
        Directory.CreateDirectory(Path.Combine(saves, "nested", "inner"));
        File.WriteAllText(Path.Combine(saves, "nested", "inner", "level.dat"), "x");

        var worlds = new ResourceTargets(store).EnumerateWorlds(LauncherPaths.GameDirectory);
        Check(worlds.Count == 1 && worlds[0] == "world1", "target: only direct child with level.dat listed");

        string dpDir = ResourceTargets.ResolveDatapackDirectory(LauncherPaths.GameDirectory, "world1");
        Check(dpDir.EndsWith("world1" + Path.DirectorySeparatorChar + "datapacks", StringComparison.Ordinal), "target: datapack dir under saves\\world\\datapacks");

        bool rejected = false;
        try { ResourceTargets.ResolveDatapackDirectory(LauncherPaths.GameDirectory, "..\\escape"); }
        catch (ResourceException) { rejected = true; }
        Check(rejected, "target: traversal world name rejected");
    }
}
