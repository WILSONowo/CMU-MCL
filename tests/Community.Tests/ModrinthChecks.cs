// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Http;
using System.Text;
using CMUmcl.Community;

internal static partial class Program
{
    private sealed class ScriptedHandler(Dictionary<string, string> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!routes.TryGetValue(request.RequestUri!.PathAndQuery, out string? body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            });
        }
    }

    private static void ModrinthChecks()
    {
        // 1. Search URL: Mod + loader fabric + game 1.20.1 + relevance + offset/limit.
        {
            var handler = new ScriptedHandler(new Dictionary<string, string>
            {
                ["/v2/search?query=&facets=%5B%5B%22project_type%3Amod%22%5D%2C%5B%22categories%3Afabric%22%5D%2C%5B%22versions%3A1.20.1%22%5D%5D&offset=0&limit=25&index=relevance"] =
                    "{\"hits\":[],\"total_hits\":0}",
            });
            using var src = new ModrinthSource(handler);
            var q = new ResourceQuery { Kind = ResourceKind.Mod, Loader = "fabric", GameVersion = "1.20.1", Sort = ResourceSort.Recommended, PageSize = 25 };
            var result = src.SearchAsync(q, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Count == 0, "modrinth: search builds facets/query correctly (no throw)");
        }

        // 2. Datapack facet is all_project_types:datapack.
        {
            var handler = new ScriptedHandler(new Dictionary<string, string>
            {
                ["/v2/search?query=&facets=%5B%5B%22all_project_types%3Adatapack%22%5D%5D&offset=0&limit=25&index=relevance"] =
                    "{\"hits\":[{\"project_id\":\"p1\",\"slug\":\"d1\",\"title\":\"Datapack A\",\"description\":\"\",\"author\":\"\",\"downloads\":1,\"versions\":[\"1.20.1\"],\"categories\":[]}],\"total_hits\":1}",
            });
            using var src = new ModrinthSource(handler);
            var q = new ResourceQuery { Kind = ResourceKind.Datapack, PageSize = 25 };
            var result = src.SearchAsync(q, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Count == 1 && result[0].Title == "Datapack A", "modrinth: datapack facet returns project");
        }

        // 3. Quilt loader facet uses OR group [quilt,fabric].
        {
            var handler = new ScriptedHandler(new Dictionary<string, string>
            {
                ["/v2/search?query=&facets=%5B%5B%22project_type%3Amod%22%5D%2C%5B%22categories%3Aquilt%22%2C%22categories%3Afabric%22%5D%5D&offset=0&limit=25&index=relevance"] =
                    "{\"hits\":[],\"total_hits\":0}",
            });
            using var src = new ModrinthSource(handler);
            var q = new ResourceQuery { Kind = ResourceKind.Mod, Loader = "quilt", PageSize = 25 };
            var result = src.SearchAsync(q, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Count == 0, "modrinth: quilt loader facet OR group");
        }

        // 4. Version endpoint: datapack uses loaders=["datapack"]; primary file selected; SHA hashes mapped.
        {
            var handler = new ScriptedHandler(new Dictionary<string, string>
            {
                ["/v2/project/d1/version?loaders=%5B%22datapack%22%5D"] =
                    "[{\"id\":\"v1\",\"version_number\":\"1.0\",\"loaders\":[\"datapack\"],\"game_versions\":[\"1.20.1\"],\"date_published\":\"2026-01-01T00:00:00Z\",\"files\":[" +
                    "{\"url\":\"https://cdn.modrinth.com/x.zip\",\"filename\":\"x.zip\",\"size\":10,\"primary\":false,\"hashes\":{\"sha1\":\"a\",\"sha512\":\"b\"}}," +
                    "{\"url\":\"https://cdn.modrinth.com/y.zip\",\"filename\":\"y.zip\",\"size\":20,\"primary\":true,\"hashes\":{\"sha1\":\"c\",\"sha512\":\"d\"}}]}]",
            });
            using var src = new ModrinthSource(handler);
            var q = new ResourceQuery { Kind = ResourceKind.Datapack };
            var versions = src.GetVersionsAsync("d1", q, CancellationToken.None).GetAwaiter().GetResult();
            Check(versions.Count == 1 && versions[0].Files.Count == 2 && versions[0].Files[0].Primary, "modrinth: version files sorted primary-first, hashes mapped");
        }

        // 5. Non-CDN file URL is dropped from the version's files.
        {
            var handler = new ScriptedHandler(new Dictionary<string, string>
            {
                ["/v2/project/p/version"] =
                    "[{\"id\":\"v1\",\"version_number\":\"1\",\"loaders\":[],\"game_versions\":[],\"files\":[" +
                    "{\"url\":\"https://evil.example.com/x.jar\",\"filename\":\"x.jar\",\"size\":1,\"primary\":true,\"hashes\":{}}]}]",
            });
            using var src = new ModrinthSource(handler);
            var q = new ResourceQuery { Kind = ResourceKind.Mod };
            var versions = src.GetVersionsAsync("p", q, CancellationToken.None).GetAwaiter().GetResult();
            Check(versions.Count == 1 && versions[0].Files.Count == 0, "modrinth: non-CDN file URL dropped");
        }

        // No-filter requests must omit loaders; explicit filters retain their semantics.
        foreach (var item in new[] {
            (loader: (string?)null, game: (string?)null, suffix: ""),
            (loader: "", game: (string?)null, suffix: ""),
            (loader: "vanilla", game: "1.21.1", suffix: "?game_versions=%5B%221.21.1%22%5D"),
            (loader: "fabric", game: "1.21.1", suffix: "?loaders=%5B%22fabric%22%5D&game_versions=%5B%221.21.1%22%5D"),
            (loader: "quilt", game: (string?)null, suffix: "?loaders=%5B%22quilt%22%2C%22fabric%22%5D") })
        {
            using var src = new ModrinthSource(new ScriptedHandler(new Dictionary<string,string> {
                ["/v2/project/filter/version" + item.suffix] = "[{\"id\":\"v1\",\"version_number\":\"1\",\"files\":[]}]"
            }));
            var found = src.GetVersionsAsync("filter", new ResourceQuery { Kind = ResourceKind.Mod, Loader = item.loader, GameVersion = item.game }, default).GetAwaiter().GetResult();
            Check(found.Count == 1, "modrinth: version filter semantics " + (item.loader ?? "absent"));
        }

        // 6. 429 short-backoff retry then success.
        {
            int calls = 0;
            var handler = new Counting429Handler(() => ++calls <= 2 ? (HttpStatusCode)429 : HttpStatusCode.OK, "{\"hits\":[],\"total_hits\":0}");
            using var src = new ModrinthSource(handler);
            var q = new ResourceQuery { Kind = ResourceKind.Mod };
            var result = src.SearchAsync(q, CancellationToken.None).GetAwaiter().GetResult();
            Check(calls == 3 && result.Count == 0, "modrinth: 429 backoff retries then succeeds");
        }
    }

    private sealed class Counting429Handler(Func<HttpStatusCode> next, string okBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var status = next();
            if (status == (HttpStatusCode)429)
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429) { Content = new ByteArrayContent(new byte[0]) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(okBody)) });
        }
    }
}
