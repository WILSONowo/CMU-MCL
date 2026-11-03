using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    private static HttpClient DirectFileClient(DirectCaptureHandler h,Func<string?>? key=null)
        => new(new SafeRedirectHandler(new CurseForgeRequestHandler(h,key ?? (()=>DirectKey),api:false),
            host=>CommunityHosts.CurseForgeCdn.Contains(host)||host=="cdn.modrinth.com",false));
    private static void DirectHeaderChecks()
    {
        foreach(string host in CommunityHosts.CurseForgeCdn)
        {
            var h=new DirectCaptureHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1})});
            using var client=DirectFileClient(h);
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://"+host+"/files/1/2/x.jar");
            request.Headers.TryAddWithoutValidation("Authorization","Bearer fixture-csu-token");
            request.Headers.TryAddWithoutValidation("X-Client-Token","fixture-client-token");
            request.Headers.TryAddWithoutValidation("Cookie","fixture-cookie");
            request.Headers.TryAddWithoutValidation("x-api-key","fixture-wrong-key");
            request.Headers.Range=new RangeHeaderValue(5,null);
            request.Headers.IfRange=new RangeConditionHeaderValue(new EntityTagHeaderValue("\"fixture-tag\""));
            client.SendAsync(request).GetAwaiter().GetResult().Dispose();
            Check(h.Requests.Count==1 && HasDirectKey(h.Requests[0]) && h.Requests[0].Headers.Range!.ToString()=="bytes=5-"
                && h.Requests[0].Headers.IfRange!=null,"direct: exact official CDN gets only project key and retains Range/If-Range");
        }
        foreach(string target in new[]{"https://mediafilez.forgecdn.net/files/1/2/x.jar","https://media.forgecdn.net/files/1/2/x.jar","https://cdn.modrinth.com/x.jar"})
        {
            var h=new DirectCaptureHandler(r=> r.RequestUri!.Host=="edge.forgecdn.net"
                ? new HttpResponseMessage(HttpStatusCode.TemporaryRedirect){Headers={Location=new Uri(target)}}
                :new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1})});
            using var client=DirectFileClient(h);
            client.GetAsync(DirectUrl).GetAwaiter().GetResult().Dispose();
            Check(h.Requests.Count==2 && HasDirectKey(h.Requests[0]) && (new Uri(target).Host=="cdn.modrinth.com"
                ? !h.Requests[1].Headers.Contains("x-api-key") : HasDirectKey(h.Requests[1])),"direct: redirect injects key per allowed CDN hop, never into Modrinth");
        }
        foreach(string target in new[]{"https://evil.example/x.jar","https://edge.forgecdn.net.evil.example/files/1/2/x.jar",
            "http://edge.forgecdn.net/files/1/2/x.jar","https://edge.forgecdn.net:444/files/1/2/x.jar",
            "https://u:p@edge.forgecdn.net/files/1/2/x.jar","https://edge.forgecdn.net/files/1/2/x.jar?api-key=fixture"})
        {
            var h=new DirectCaptureHandler(_=>new HttpResponseMessage(HttpStatusCode.TemporaryRedirect){Headers={Location=new Uri(target)}});
            using var client=DirectFileClient(h);bool stopped=false;
            try{client.GetAsync(DirectUrl).GetAwaiter().GetResult();}catch(HttpRequestException){stopped=true;}
            Check(stopped && h.Requests.Count==1,"direct: unsafe redirect rejected before credentials reach destination");
        }
        var unavailable=new DirectCaptureHandler(_=>new HttpResponseMessage(HttpStatusCode.OK));
        string currentKey=DirectKey;
        var rotate=new DirectCaptureHandler(r=>
        {
            if(r.RequestUri!.Host=="edge.forgecdn.net")
            {
                currentKey="fixture-rotated-project-key";
                return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect){Headers={Location=new Uri("https://mediafilez.forgecdn.net/files/1/2/x.jar")}};
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using(var client=DirectFileClient(rotate,()=>currentKey))client.GetAsync(DirectUrl).GetAwaiter().GetResult().Dispose();
        Check(rotate.Requests.Count==2 && HasDirectKey(rotate.Requests[0])
            && rotate.Requests[1].Headers.GetValues("x-api-key").Single()=="fixture-rotated-project-key",
            "direct: credentials are read anew at each validated redirect hop");
        using(var client=DirectFileClient(unavailable,()=>null))
        {bool stopped=false;try{client.GetAsync(DirectUrl).GetAwaiter().GetResult();}catch(HttpRequestException){stopped=true;}
         Check(stopped&&unavailable.Requests.Count==0,"direct: missing file key fails before network");}
        var publicDownload=new DirectCaptureHandler(_=>new HttpResponseMessage(HttpStatusCode.OK));
        using(var client=DirectFileClient(publicDownload,()=>throw new Exception("must not read key for public files")))
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://cdn.modrinth.com/x.jar");
            request.Headers.TryAddWithoutValidation("x-api-key",DirectKey);request.Headers.TryAddWithoutValidation("Authorization","fixture");
            client.SendAsync(request).GetAwaiter().GetResult().Dispose();
            Check(!publicDownload.Requests[0].Headers.Contains("x-api-key")&&!publicDownload.Requests[0].Headers.Contains("Authorization"),"direct: Modrinth neither reads nor receives credentials");
        }
        var icons=new DirectCaptureHandler(_=>new HttpResponseMessage(HttpStatusCode.NotFound));
        using(var loader=new ResourceIconLoader(icons))loader.LoadAsync("https://media.forgecdn.net/avatars/1/icon.png",default).GetAwaiter().GetResult();
        Check(icons.Requests.Count==1 && !icons.Requests[0].Headers.Contains("x-api-key"),"direct: icons stay credential-free");

        string testRoot=Path.Combine(Path.GetTempPath(),"CMUmcl-direct-http-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        foreach(int status in new[]{401,403})
        {
            var h=new DirectCaptureHandler(_=>new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent("fixture-private-error")});
            using var client=DirectFileClient(h);bool failed=false;
            try{ResumableDownload.DownloadAsync(client,DirectUrl,Path.Combine(testRoot,status+".jar"),new DownloadExpectation(12,Sha1:DirectSha1),null,default).GetAwaiter().GetResult();}
            catch(HttpRequestException e){failed=!e.Message.Contains("fixture-private-error")&&!e.Message.Contains("登录");}
            Check(failed&&h.Requests.Count==1,"direct: CDN auth/permission failure is not retried or presented as CSU login expiry");
        }
        using(var cancel=new CancellationTokenSource())
        {
            var h=new DirectCaptureHandler(_=>{var r=new HttpResponseMessage((HttpStatusCode)429);r.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));return r;});
            using var client=DirectFileClient(h);cancel.CancelAfter(100);bool stopped=false;
            try{ResumableDownload.DownloadAsync(client,DirectUrl,Path.Combine(testRoot,"limited.jar"),new DownloadExpectation(12,Sha1:DirectSha1),null,cancel.Token).GetAwaiter().GetResult();}
            catch(OperationCanceledException){stopped=true;}
            Check(stopped&&h.Requests.Count==1,"direct: file Retry-After blocks early retry and remains cancellable");
        }
        DirectMrpackChecks();
        DirectFiveStepChecks();
    }
    private static void DirectMrpackChecks()
    {
        string root=Path.Combine(Path.GetTempPath(),"CMUmcl-direct-mrpack-"+Guid.NewGuid().ToString("N"));
        LauncherPaths.RootOverride=root;LauncherPaths.EnsureDirectories();
        byte[] payload=Encoding.UTF8.GetBytes("fixture mrpack CF payload");
        string hash=BitConverter.ToString(SHA1.Create().ComputeHash(payload)).Replace("-","").ToLowerInvariant();
        string archive=Path.Combine(root,"fixture.mrpack");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        using(var writer=new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write(System.Text.Json.JsonSerializer.Serialize(new {formatVersion=1,game="minecraft",versionId="fixture",name="fixture",
                dependencies=new Dictionary<string,string>{{"minecraft","1.21.1"}},files=new[]{new{path="mods/fixture.jar",hashes=new{sha1=hash},fileSize=payload.Length,downloads=new[]{DirectUrl}}}}));
        var h=new DirectCaptureHandler(r=>new HttpResponseMessage(HasDirectKey(r)?HttpStatusCode.OK:HttpStatusCode.Unauthorized){Content=new ByteArrayContent(payload)});
        using var import=new MrpackImportService(new InstalledStateStore(),Path.Combine(root,"isolated-instances"),h,()=>DirectKey);
        var result=import.ImportAsync(archive,ct:default).GetAwaiter().GetResult();
        Check(File.ReadAllBytes(Path.Combine(result.InstanceDirectory,"mods/fixture.jar")).SequenceEqual(payload)&&h.Requests.All(HasDirectKey),
            "direct: real mrpack importer authenticates existing official CF URL and verifies payload in isolated instance");
    }

    private static void DirectFiveStepChecks()
    {
        string root=Path.Combine(Path.GetTempPath(),"CMUmcl-direct-chain-"+Guid.NewGuid().ToString("N"));
        LauncherPaths.RootOverride=root;LauncherPaths.EnsureDirectories();
        byte[] payload=Encoding.UTF8.GetBytes("fixture complete direct chain");
        string hash=BitConverter.ToString(SHA1.Create().ComputeHash(payload)).Replace("-","").ToLowerInvariant();
        var all=new List<HttpRequestMessage>();
        var metadata=new{id=2,modId=1,isAvailable=true,fileLength=payload.Length,fileName="fixture.zip",hashes=new[]{new{algo=1,value=hash}},gameVersions=new[]{"1.21.1"}};
        HttpResponseMessage Respond(HttpRequestMessage r)
        {
            all.Add(r);
            if(!HasDirectKey(r))return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            string path=r.RequestUri!.AbsolutePath;
            if(r.RequestUri.Host=="edge.forgecdn.net")return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)};
            if(path=="/v1/mods/search")return DirectJson("{\"data\":[{\"id\":1,\"name\":\"fixture\"}]}");
            if(path=="/v1/mods/1")return DirectJson("{\"data\":{\"id\":1,\"name\":\"fixture\",\"allowModDistribution\":true}}");
            if(path=="/v1/mods/1/files")return DirectJson(System.Text.Json.JsonSerializer.Serialize(new{data=new[]{metadata}}));
            if(path=="/v1/mods/1/files/2")return DirectJson(System.Text.Json.JsonSerializer.Serialize(new{data=metadata}));
            return DirectJson(System.Text.Json.JsonSerializer.Serialize(new{data=DirectUrl}));
        }
        using var src=new CurseForgeSource(DirectKey,new DirectCaptureHandler(Respond));
        var projects=src.SearchAsync(new ResourceQuery{Kind=ResourceKind.Modpack},default).GetAwaiter().GetResult();
        var project=src.GetProjectAsync(projects.Single().Id,default).GetAwaiter().GetResult();
        var version=src.GetVersionsAsync(project.Id,new ResourceQuery(),default).GetAwaiter().GetResult().Single();
        var file=src.ResolveDownloadAsync(project.Id,long.Parse(version.Id),version.Files.Single(),default).GetAwaiter().GetResult()!;
        var store=new InstalledStateStore();
        using var service=new ResourceInstallService(new ResourceTargets(store),new MrpackImportService(store),new DirectCaptureHandler(Respond),()=>DirectKey);
        string target=Path.Combine(root,"verified.zip");
        var result=service.InstallAsync(new CommunityInstallRequest{Project=project,Version=version,File=file,Kind=ResourceKind.Modpack,ModpackSavePath=target},null,default).GetAwaiter().GetResult();
        Check(result.Success && File.ReadAllBytes(target).SequenceEqual(payload),"direct: synthetic search/project/version/grant/file chain commits verified bytes");
        Check(all.All(r=>HasDirectKey(r) && (r.RequestUri!.Host=="api.curseforge.com"||r.RequestUri.Host=="edge.forgecdn.net")),
            "direct: full chain needs only project key, no CSU provider or launcher backend");
    }
}
