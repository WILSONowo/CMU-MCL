// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void SetupTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "CMUmcl-community-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LauncherPaths.RootOverride = root;
        LauncherPaths.EnsureDirectories();
    }

    private static async Task<int> Main(string[] args)
    {
        if (args != null)
        {
            foreach (string arg in args)
            {
                if (arg == "--live-modrinth") return await RunLiveModrinthAsync();
                if (arg == "--live-curseforge") return RunLiveCurseForgeAsync();
            }
        }

        try
        {
            SetupTempRoot();

            RedirectChecks();
            ModelChecks();
            await DependencyChecks();
            ModrinthChecks();
            CurseForgeChecks();
            DirectSourceChecks();
            DirectDownloadChecks();
            ResolverChecks();
            TargetChecks();
            CompatibilityChecks();
            CatalogChecks();
            PartialMessageChecks();
            InstallChecks();
            IconChecks();

            Console.WriteLine("PASS " + _passed + " community checks");
            return 0;
        }
        catch (Exception ex)
        {
            PrintException(ex);
            return 1;
        }
    }

    private static void PrintException(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            Console.WriteLine(e.GetType().FullName);
            Console.WriteLine(e.Message);
        }
        Console.WriteLine(ex.StackTrace);
    }

    // ---------------------------------------------------------------- live checks (read-only, no install)

    /// <summary>
    /// Read-only live check against the public Modrinth v2 API (no key). Searches one Mod and one
    /// Modpack, fetches their versions, and asserts at least one file URL host is cdn.modrinth.com
    /// and that a SHA-1/SHA-512 hash is present. Never downloads or installs anything and never
    /// touches a real instance (temp RootOverride only).
    /// </summary>
    private static async Task<int> RunLiveModrinthAsync()
    {
        try
        {
            SetupTempRoot();

            using var src = new ModrinthSource();

            ResourceProject mod = await SearchOneAsync(src, ResourceKind.Mod, "sodium");
            IReadOnlyList<ResourceVersion> modVersions = await src.GetVersionsAsync(
                mod.Id, new ResourceQuery { Kind = ResourceKind.Mod, Loader = "fabric" }, CancellationToken.None);
            AssertLiveVersions("mod", "sodium", modVersions);

            ResourceProject pack = await SearchOneAsync(src, ResourceKind.Modpack, "fabulously-optimized");
            IReadOnlyList<ResourceVersion> packVersions = await src.GetVersionsAsync(
                pack.Id, new ResourceQuery { Kind = ResourceKind.Modpack, Loader = "fabric" }, CancellationToken.None);
            AssertLiveVersions("modpack", "fabulously-optimized", packVersions);

            Console.WriteLine("PASS live-modrinth: one Mod + one Modpack searched; cdn.modrinth.com file URLs and SHA-1/SHA-512 hashes verified (read-only, no install)");
            return 0;
        }
        catch (Exception ex)
        {
            PrintException(ex);
            Console.WriteLine("FAIL live-modrinth: " + ex.Message);
            return 1;
        }
    }

    private static async Task<ResourceProject> SearchOneAsync(ModrinthSource src, ResourceKind kind, string searchText)
    {
        var query = new ResourceQuery { Kind = kind, SearchText = searchText, Sort = ResourceSort.Downloads, PageSize = 5 };
        IReadOnlyList<ResourceProject> results = await src.SearchAsync(query, CancellationToken.None);
        if (results.Count == 0)
            throw new Exception("no search results for " + kind + " '" + searchText + "'");
        Console.WriteLine("PASS live-modrinth: search " + kind + " '" + searchText + "' -> " + results.Count + " hits");
        return results[0];
    }

    private static void AssertLiveVersions(string kindLabel, string searchText, IReadOnlyList<ResourceVersion> versions)
    {
        if (versions.Count == 0)
            throw new Exception("no versions for " + kindLabel + " '" + searchText + "'");

        bool cdnSeen = false;
        bool hashSeen = false;
        string? firstUrl = null;
        foreach (ResourceVersion v in versions)
        {
            foreach (ResourceFile f in v.Files)
            {
                if (!string.IsNullOrWhiteSpace(f.Url))
                {
                    firstUrl ??= f.Url;
                    string host = "";
                    try { host = new Uri(f.Url).Host; } catch { /* ignore malformed url */ }
                    if (string.Equals(host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase)) cdnSeen = true;
                }
                if (f.HasSecureHash) hashSeen = true;
            }
        }

        if (!cdnSeen)
            throw new Exception("no cdn.modrinth.com file URL for " + kindLabel + " '" + searchText + "' (first url: " + (firstUrl ?? "<none>") + ")");
        if (!hashSeen)
            throw new Exception("no SHA-1/SHA-512 hash present for " + kindLabel + " '" + searchText + "'");

        Console.WriteLine("PASS live-modrinth: " + kindLabel + " '" + searchText + "' versions=" + versions.Count + " cdn.modrinth.com=seen sha1/sha512=present");
    }

    // No live key, CSU session or old proxy tool is used by this local regression entry.
    private static int RunLiveCurseForgeAsync()
    {
        Console.WriteLine("SKIP live-curseforge: direct source production flag remains false; real-key acceptance has not run");
        return 0;
    }
}
