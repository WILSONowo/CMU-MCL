using System.Net;
using System.Net.Http;
using System.Text;
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    private static readonly string DirectSha1 = new string('a', 40);
    private const string DirectUrl = "https://edge.forgecdn.net/files/1/2/x.jar";
    private const string DirectKey = "fixture-direct-project-key";
    private static HttpResponseMessage DirectJson(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage DirectFixture(HttpRequestMessage request, string? downloadUrl = DirectUrl, string distribution = "true", string? metadata = null)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/v1/mods/1") return DirectJson("{\"data\":{\"id\":1,\"allowModDistribution\":" + distribution + "}}");
        if (path == "/v1/mods/1/files/2") return DirectJson(metadata ?? ("{\"data\":{\"id\":2,\"modId\":1,\"isAvailable\":true,\"fileLength\":12,\"fileName\":\"x.jar\",\"hashes\":[{\"algo\":1,\"value\":\"" + DirectSha1 + "\"}]}}"));
        if (path.EndsWith("/download-url")) return DirectJson(System.Text.Json.JsonSerializer.Serialize(new { data = downloadUrl }));
        return DirectJson("{\"data\":[]}");
    }
    private static void DirectSourceChecks()
    {
        Check(CommunityFeatures.CurseForgeEnabled, "direct: local candidate feature enabled");
        using (var production = new CurseForgeSource()) Check(!production.IsAvailable, "direct: ordinary build without embedded key remains unavailable");
        int keyReads = 0;
        var disabledTransport = new DirectCaptureHandler(r => DirectFixture(r));
        using (var disabled = new CurseForgeSource(() => { keyReads++; return DirectKey; }, disabledTransport, false))
        {
            bool blocked = false;
            try { disabled.SearchAsync(new ResourceQuery(), default).GetAwaiter().GetResult(); } catch (CommunitySourceException) { blocked = true; }
            Check(blocked && keyReads == 0 && disabledTransport.Requests.Count == 0, "direct: false flag prevents key reads and network even with configured provider");
        }
        var transport = new DirectCaptureHandler(r => DirectFixture(r));
        using (var src = new CurseForgeSource(DirectKey, transport))
        {
            src.SearchAsync(new ResourceQuery { GameVersion = "1.21.1", Loader = "fabric" }, default).GetAwaiter().GetResult();
            src.GetProjectAsync("1", default).GetAwaiter().GetResult();
            src.GetVersionsAsync("1", new ResourceQuery(), default).GetAwaiter().GetResult();
            ResourceFile? f = src.ResolveDownloadAsync("1", 2, new ResourceFile { FileName="stale.jar", Size=1, Sha1="bad" }, default).GetAwaiter().GetResult();
            Check(f?.Url == DirectUrl && f.Sha1 == DirectSha1 && f.Size == 12 && f.FileName == "x.jar", "direct: official file grant uses fresh metadata, not stale template");
            Check(transport.Requests.All(r => r.RequestUri!.Host == "api.curseforge.com" && HasDirectKey(r)), "direct: all metadata uses official API and project key, no CSU credentials");
            Check(transport.Requests.All(r => !r.RequestUri!.Query.Contains("api-key")), "direct: key is never a URL parameter");
        }
        foreach (string permission in new[] { "false", "null", "true" })
        {
            var h = new DirectCaptureHandler(r => DirectFixture(r, distribution:permission));
            using var src = new CurseForgeSource(DirectKey, h);
            ResourceFile? f = src.ResolveDownloadAsync("1", 2, new ResourceFile(), default).GetAwaiter().GetResult();
            Check((f != null) == (permission != "false"), "direct: distribution permission " + permission);
            if (permission == "false") Check(h.Requests.Count == 1, "direct: author refusal blocks before file URL grant");
        }
        foreach (string? url in new[] { null, "https://evil.example/files/1/2/x.jar", "http://edge.forgecdn.net/files/1/2/x.jar",
            "https://edge.forgecdn.net:444/files/1/2/x.jar", "https://u:p@edge.forgecdn.net/files/1/2/x.jar",
            "https://edge.forgecdn.net/files/1/2/x.jar?api-key=fixture", "https://edge.forgecdn.net/files/1/2/x.jar?api%2Dkey=fixture", "https://edge.forgecdn.net/files/1/2/x.jar#fragment" })
        {
            using var src = new CurseForgeSource(DirectKey, new DirectCaptureHandler(r => DirectFixture(r, downloadUrl:url)));
            Check(src.ResolveDownloadAsync("1",2,new ResourceFile(),default).GetAwaiter().GetResult() == null, "direct: invalid official grant rejected without synthesizing URL");
        }
        string valid = "{\"data\":{\"id\":2,\"modId\":1,\"isAvailable\":true,\"fileLength\":12,\"fileName\":\"x.jar\",\"hashes\":[{\"algo\":1,\"value\":\"" + DirectSha1 + "\"}]}}";
        foreach (string bad in new[] { valid.Replace("\"modId\":1", "\"modId\":9"), valid.Replace("\"id\":2", "\"id\":9"), valid.Replace("true", "false"),
            valid.Replace("\"fileLength\":12", "\"fileLength\":2147483649"), valid.Replace(DirectSha1,"invalid"), valid.Replace("\"algo\":1", "\"algo\":2") })
        {
            var h = new DirectCaptureHandler(r => DirectFixture(r,metadata:bad));
            using var src = new CurseForgeSource(DirectKey,h);
            Check(src.ResolveDownloadAsync("1",2,new ResourceFile(),default).GetAwaiter().GetResult()==null && h.Requests.Count == 2,
                "direct: unavailable/mismatched/oversize/unhashed file rejected before grant");
        }
        var invalidId = new DirectCaptureHandler(r => DirectFixture(r));
        using (var src = new CurseForgeSource(DirectKey,invalidId))
        {
            bool blocked=false;
            try { src.GetProjectAsync("1/evil",default).GetAwaiter().GetResult(); } catch(CommunitySourceException) { blocked=true; }
            Check(blocked && invalidId.Requests.Count==0,"direct: invalid ID rejected before network");
        }
        foreach (string? retry in new[] { "120", "0", "Wed, 01 Jan 2031 00:00:00 GMT", "bad", "-1", null })
        {
            var waits=new List<TimeSpan>(); int calls=0;
            var h=new DirectCaptureHandler(r=> { if(++calls>1)return DirectJson("{\"data\":[]}"); var response=new HttpResponseMessage((HttpStatusCode)429) { Content=new StringContent("fixture-private-error") };
                if(retry!=null)response.Headers.TryAddWithoutValidation("Retry-After",retry);return response; });
            using var src=new CurseForgeSource(DirectKey,h,(wait,ct)=>{waits.Add(wait);ct.ThrowIfCancellationRequested();return Task.CompletedTask;});
            src.SearchAsync(new ResourceQuery(),default).GetAwaiter().GetResult();
            Check(calls==2 && waits.Count==1 && (retry=="120" ? waits[0]==TimeSpan.FromSeconds(120) : retry=="0" ? waits[0]==TimeSpan.Zero : retry?.StartsWith("Wed")==true ? waits[0]>TimeSpan.FromDays(1) : waits[0]==TimeSpan.FromMilliseconds(300)),
                "direct: metadata 429 honors validated Retry-After or fallback");
        }
        using var cancelled=new CancellationTokenSource();
        var retryTransport=new DirectCaptureHandler(_=>{var response=new HttpResponseMessage((HttpStatusCode)429);response.Headers.TryAddWithoutValidation("Retry-After","120");return response;});
        using(var src=new CurseForgeSource(DirectKey,retryTransport,(wait,ct)=>{cancelled.Cancel();ct.ThrowIfCancellationRequested();return Task.CompletedTask;}))
        {
            bool stopped=false;try{src.SearchAsync(new ResourceQuery(),cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}
            Check(stopped && retryTransport.Requests.Count==1,"direct: cancellation stops Retry-After waiting before a second request");
        }
        foreach (int code in new[] {401,403,503})
        {
            var h=new DirectCaptureHandler(_=>new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent("fixture-private-error")});
            using var src=new CurseForgeSource(DirectKey,h); CommunitySourceException? failure=null;
            try{src.SearchAsync(new ResourceQuery(),default).GetAwaiter().GetResult();}catch(CommunitySourceException e){failure=e;}
            Check(failure?.HttpStatus==code && h.Requests.Count==1 && !failure.Message.Contains("fixture-private-error") && !failure.Message.Contains("登录"),"direct: source errors are safe and do not request CSU re-login");
        }
    }
    private static bool HasDirectKey(HttpRequestMessage r) => r.Headers.TryGetValues("x-api-key",out var keys) && keys.Single()==DirectKey
        && !r.Headers.Contains("Authorization") && !r.Headers.Contains("X-Client-Token") && !r.Headers.Contains("Cookie");
    private sealed class DirectCaptureHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests=new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Requests.Add(r);return Task.FromResult(respond(r));}
    }
}
