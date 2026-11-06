using System.Net;
using CMUmcl.Community;
using CMUmcl.Content;

// User feedback: slow community page, instances that cannot be deleted.
internal static class FeedbackFixTests
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "cmumcl-feedback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await ProgressiveSearchChecks(check);
            await IconCacheChecks(root, check);
            RemovalChecks(root, check);
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }

    private static async Task ProgressiveSearchChecks(Action<bool, string> check)
    {
        var slowGate = new TaskCompletionSource<bool>();
        var fast = new FakeSource(ResourceSourceId.Modrinth, Task.FromResult(true), "fast");
        var slow = new FakeSource(ResourceSourceId.CurseForge, slowGate.Task, "slow");
        var catalog = new ResourceCatalog(fast, slow);
        var partial = new TaskCompletionSource<IReadOnlyList<ResourceProject>>();
        var search = catalog.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod, PageSize = 50 }, null, null, CancellationToken.None,
            projects => partial.TrySetResult(projects));
        var first = await Task.WhenAny(partial.Task, Task.Delay(5000));
        check(first == partial.Task && partial.Task.Result.All(p => p.Id.StartsWith("fast")) && !search.IsCompleted,
            "community: faster source is shown before the slower source answers");
        slowGate.SetResult(true);
        var page = await search;
        check(page.Projects.Count == 4 && page.Projects[0].Source == ResourceSourceId.Modrinth && page.Projects[3].Source == ResourceSourceId.CurseForge,
            "community: final page merges both sources in source order");
    }

    private static async Task IconCacheChecks(string root, Action<bool, string> check)
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        string cache = Path.Combine(root, "icons");
        int requests = 0;
        const string url = "https://cdn.modrinth.com/data/fixture/icon.png";
        using (var online = new ResourceIconLoader(new Reply(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) }; }), cache))
            check(await online.LoadAsync(url, CancellationToken.None) != null && requests == 1, "icons: first load downloads the icon");
        using (var offline = new ResourceIconLoader(new Reply(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }), cache))
            check(await offline.LoadAsync(url, CancellationToken.None) != null && requests == 1, "icons: later launches load the icon from disk without a request");
        check(Directory.GetFiles(cache, "*.img").Length == 1 && Directory.GetFiles(cache, "*.tmp").Length == 0, "icons: one cache file, no leftovers");
    }

    private static void RemovalChecks(string root, Action<bool, string> check)
    {
        string? oldRoot = LauncherPaths.RootOverride;
        LauncherPaths.RootOverride = Path.Combine(root, "data");
        string? recycled = null;
        InstanceRemoval.RecycleOverride = path => { recycled = path; Directory.Delete(path, true); };
        try
        {
            var installed = new InstalledStateStore();
            string dir = Path.Combine(LauncherPaths.InstancesDirectory, "to-delete");
            Directory.CreateDirectory(Path.Combine(dir, "saves", "world"));
            File.WriteAllText(Path.Combine(dir, "instance.json"), "{\"id\":\"local:to-delete\",\"game\":\"1.21.1\"}");
            installed.RecordInstall("local:to-delete", 1, "to-delete", "待删除");
            installed.RecordInstall("local:keep", 1, "keep", "保留");
            InstanceRemoval.Remove(installed, "local:to-delete", dir);
            check(recycled == Path.GetFullPath(dir) && !Directory.Exists(dir), "delete: instance folder goes to the recycle bin");
            check(!installed.ListRecords().ContainsKey("local:to-delete") && installed.ListRecords().ContainsKey("local:keep"), "delete: only the deleted record is removed");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            installed.RecordInstall("local:outside", 1, "outside", null);
            bool refused = false;
            try { InstanceRemoval.Remove(installed, "local:outside", outside); } catch (IOException) { refused = true; }
            check(refused && Directory.Exists(outside) && installed.ListRecords().ContainsKey("local:outside"), "delete: folders outside the instances directory are never touched");
        }
        finally { InstanceRemoval.RecycleOverride = null; LauncherPaths.RootOverride = oldRoot; }
    }

    private sealed class FakeSource(ResourceSourceId id, Task gate, string prefix) : IResourceSource
    {
        public ResourceSourceId SourceId => id;
        public bool IsAvailable => true;
        public async Task<IReadOnlyList<ResourceProject>> SearchAsync(ResourceQuery query, CancellationToken ct)
        {
            await gate.ConfigureAwait(false);
            return new[] { new ResourceProject { Source = id, Id = prefix + "-1", Title = prefix + " 1" }, new ResourceProject { Source = id, Id = prefix + "-2", Title = prefix + " 2" } };
        }
        public Task<ResourceProject> GetProjectAsync(string projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(string projectId, ResourceQuery query, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = reply(request); response.RequestMessage = request; return Task.FromResult(response);
        }
    }
}
