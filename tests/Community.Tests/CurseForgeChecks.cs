// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Http;
using System.Text;
using CMUmcl.Community;

internal static partial class Program
{
    private static void CurseForgeChecks()
    {
        using(var fallback=new CurseForgeSource("fixture",new CapturingHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":1,\"name\":\"fallback\",\"logo\":{\"thumbnailUrl\":null,\"url\":\"https://media.forgecdn.net/original.png\"}}]}")})))
            Check(fallback.SearchAsync(new ResourceQuery{Kind=ResourceKind.Modpack},default).GetAwaiter().GetResult().Single().IconUrl=="https://media.forgecdn.net/original.png","cf: missing thumbnail falls back to official original logo");
        // 1. Missing key → IsAvailable false, no requests issued.
        {
            int calls = 0;
            var handler = new CountingHandler(() => { Interlocked.Increment(ref calls); }, HttpStatusCode.OK, "{\"data\":[]}");
            using var src = new CurseForgeSource(null, handler);
            Check(!src.IsAvailable, "cf: missing key disables source");
            bool threw = false;
            try { src.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (CommunitySourceException) { threw = true; }
            Check(threw && calls == 0, "cf: disabled source makes no request");
        }

        // 2. Key injected only for api.curseforge.com.
        {
            string? seenKey = null;
            string? seenHost = null;
            var handler = new CapturingHandler(req =>
            {
                seenHost = req.RequestUri!.Host;
                if (req.Headers.TryGetValues("x-api-key", out var v)) seenKey = v.FirstOrDefault();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"data\":[]}")) };
            });
            using var src = new CurseForgeSource("SECRET-KEY", handler);
            src.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod }, CancellationToken.None).GetAwaiter().GetResult();
            Check(seenHost == CommunityHosts.CurseForgeApi && seenKey == "SECRET-KEY", "cf: key injected for api.curseforge.com");
        }

        // 3. Key isolation: a cross-host redirect is rejected before the key handler runs.
        {
            var handler = new CapturingHandler(req =>
                new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://cdn.modrinth.com/x") } });
            using var src = new CurseForgeSource("SECRET-KEY", handler);
            bool rejected = false;
            try { src.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception) { rejected = true; }
            Check(rejected, "cf: cross-host redirect rejected (key never forwarded)");
        }

        // 4. Search URL: classId + modLoaderType gated on gameVersion + sort mapping + pageSize<=50.
        {
            string? lastQuery = null;
            var handler = new CapturingHandler(req => { lastQuery = req.RequestUri!.PathAndQuery; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"data\":[]}")) }; });
            using var src = new CurseForgeSource("k", handler);
            src.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod, Loader = "fabric", GameVersion = "1.20.1", Sort = ResourceSort.Downloads, PageSize = 50 }, CancellationToken.None).GetAwaiter().GetResult();
            Check(lastQuery!.Contains("gameId=432") && lastQuery.Contains("classId=6") && lastQuery.Contains("modLoaderType=4")
                && lastQuery.Contains("gameVersion=1.20.1") && lastQuery.Contains("sortField=6") && lastQuery.Contains("pageSize=50"), "cf: search params incl. modLoaderType gated on gameVersion");

            string? q2 = null;
            var handler2 = new CapturingHandler(req => { q2 = req.RequestUri!.PathAndQuery; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"data\":[]}")) }; });
            using var src2 = new CurseForgeSource("k", handler2);
            src2.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod, Loader = "fabric", Sort = ResourceSort.Name }, CancellationToken.None).GetAwaiter().GetResult();
            Check(q2!.Contains("sortField=4") && q2.Contains("sortOrder=asc") && !q2.Contains("modLoaderType") && !q2.Contains("gameVersion"), "cf: no gameVersion → no modLoaderType/gameVersion");
        }

        // 5. FileHash algo==1 → SHA-1; algo==2 (MD5) ignored; sortableGameVersions objects → loader mapping.
        {
            var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                    "{\"data\":[{\"id\":1,\"fileName\":\"x.jar\",\"fileLength\":10,\"hashes\":[{\"value\":\"ABCDEF\",\"algo\":1},{\"value\":\"9999\",\"algo\":2}],\"gameVersions\":[\"1.20.1\"],\"sortableGameVersions\":[{\"gameVersionName\":\"1.20.1\",\"gameVersionTypeId\":0},{\"gameVersionName\":\"Fabric 0.15.0\",\"gameVersionTypeId\":0}],\"isAvailable\":true}]}")),
            });
            using var src = new CurseForgeSource("k", handler);
            var versions = src.GetVersionsAsync("1", new ResourceQuery { Kind = ResourceKind.Mod }, CancellationToken.None).GetAwaiter().GetResult();
            Check(versions.Count == 1 && versions[0].Files[0].Sha1 == "ABCDEF" && versions[0].Files[0].Sha512 == null, "cf: SHA-1 parsed, MD5 ignored");
            Check(versions[0].Loaders.Count == 1 && versions[0].Loaders.Contains("fabric"), "cf: sortableGameVersions object → fabric loader");
        }

        // 6. download-url endpoint resolves URL; empty data → null.
        {
            var handler = new CapturingHandler(r => DirectFixture(r, downloadUrl: "https://mediafilez.forgecdn.net/files/1/2/x.jar"));
            using var src = new CurseForgeSource("k", handler);
            var template = new ResourceFile { FileName = "x.jar", Size = 10, Sha1 = "a" };
            var file = src.ResolveDownloadAsync("1", 2, template, CancellationToken.None).GetAwaiter().GetResult();
            Check(file != null && file.Url == "https://mediafilez.forgecdn.net/files/1/2/x.jar", "cf: download-url resolves");

            var handler2 = new CapturingHandler(r => DirectFixture(r, downloadUrl: null));
            using var src2 = new CurseForgeSource("k", handler2);
            var file2 = src2.ResolveDownloadAsync("1", 2, template, CancellationToken.None).GetAwaiter().GetResult();
            Check(file2 == null, "cf: null download-url → not downloadable");
        }

        // 7. 401 maps to "invalid key" (no retry).
        {
            int calls = 0;
            var handler = new CountingHandler(() => { Interlocked.Increment(ref calls); }, HttpStatusCode.Unauthorized, "{}");
            using var src = new CurseForgeSource("k", handler);
            CommunitySourceException? ex = null;
            try { src.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (CommunitySourceException e) { ex = e; }
            Check(ex != null && ex.HttpStatus == 401 && calls == 1, "cf: 401 invalid key, no retry");
        }
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(reply(request));
    }

    private sealed class CountingHandler(Action bump, HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            bump();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) });
        }
    }
}
