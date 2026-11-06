// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Content;

/// <summary>
/// Session-wide cache of the version lists shown in dropdowns, so opening a list is instant after
/// the first load and several editors share one request. Failed or empty loads are retried.
/// </summary>
internal static class VersionChoices
{
    private static readonly object Gate = new();
    private static VersionCatalogService? _catalog;
    private static Task<string[]>? _games;
    private static readonly Dictionary<string, Task<string[]>> Loaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Test hook: (loader or null for game versions, game version) → list.</summary>
    internal static Func<string?, string, Task<string[]>>? Override { get; set; }

    private static VersionCatalogService Catalog { get { lock (Gate) return _catalog ??= new VersionCatalogService(); } }

    /// <summary>Release versions, newest first.</summary>
    internal static Task<string[]> GameVersionsAsync()
    {
        if (Override != null) return Override(null, "");
        lock (Gate)
        {
            if (_games == null || Unusable(_games)) _games = LoadGamesAsync();
            return _games;
        }
    }

    /// <summary>Loader builds compatible with a game version, recommended first.</summary>
    internal static Task<string[]> LoaderVersionsAsync(string loader, string gameVersion)
    {
        if (Override != null) return Override(loader, gameVersion);
        string key = loader + "|" + gameVersion;
        lock (Gate)
        {
            if (!Loaders.TryGetValue(key, out var task) || Unusable(task)) Loaders[key] = task = LoadLoadersAsync(loader, gameVersion);
            return task;
        }
    }

    internal static void Reset() { lock (Gate) { _games = null; Loaders.Clear(); } }

    private static bool Unusable(Task<string[]> task) => task.IsFaulted || task.IsCanceled || (task.Status == TaskStatus.RanToCompletion && task.Result.Length == 0);

    private static async Task<string[]> LoadGamesAsync()
    {
        var all = await Catalog.GetMinecraftVersionsAsync().ConfigureAwait(false);
        return all.Where(v => v.Category == MinecraftVersionCategory.Release).Select(v => v.Name).ToArray();
    }

    private static async Task<string[]> LoadLoadersAsync(string loader, string gameVersion)
    {
        var all = await Catalog.GetLoaderVersionsAsync(loader, gameVersion).ConfigureAwait(false);
        return all.Select(v => v.Version).Distinct(StringComparer.Ordinal).ToArray();
    }
}
