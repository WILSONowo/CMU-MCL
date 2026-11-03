using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CMUmcl.Community;

internal static partial class Program
{
    private sealed class LocalIconTransport(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(reply(request));}
    private static async Task LocalModIconChecks(string source)
    {
        string jar=Path.Combine(_output,"test.jar");byte[] bytes=Encoding.UTF8.GetBytes("exact file identity fixture");File.WriteAllBytes(jar,bytes);
        string hash;using(var sha=SHA1.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        int api=0,images=0;bool clean=true;
        HttpResponseMessage Json(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"application/json")};
        using(var resolver=new LocalModIconResolver(new LocalIconTransport(request=>
        {
            api++;clean&=!request.Headers.Contains("Authorization")&&!request.Headers.Contains("x-api-key")&&request.Content==null;
            if(request.RequestUri!.AbsolutePath.StartsWith("/v2/version_file/")){Check(request.RequestUri.AbsolutePath.EndsWith(hash)&&request.RequestUri.Query=="?algorithm=sha1","mod icon: sends only exact SHA-1 hash to version endpoint");return Json("{\"project_id\":\"fixture\"}");}
            Check(request.RequestUri.AbsolutePath=="/v2/project/fixture","mod icon: resolved project endpoint");return Json("{\"icon_url\":\"https://cdn.modrinth.com/icon.webp\"}");
        }),new LocalIconTransport(_=>{images++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(Path.Combine(source,"tests/LauncherSettings.Tests/Fixtures/sodium.webp")))};})))
        {
            var result=await resolver.LoadAsync(jar);Check(result!=null&&result.IsFrozen,"mod icon: exact match returns decoded frozen image");
            Check(ReferenceEquals(result,await resolver.LoadAsync(jar))&&api==2&&images==1,"mod icon: cached repeat avoids network");
            File.Move(jar,jar+".disabled");Check(ReferenceEquals(result,await resolver.LoadAsync(jar+".disabled"))&&api==2,"mod icon: disabled file keeps same project icon");File.Move(jar+".disabled",jar);
            Check(clean,"mod icon: no API key, authentication or file body is sent");
        }
        using(var missing=new LocalModIconResolver(new LocalIconTransport(_=>new(HttpStatusCode.NotFound)),new LocalIconTransport(_=>throw new Exception("unexpected image request"))))Check(await missing.LoadAsync(jar)==null,"mod icon: unknown hash uses default");
        using(var absent=new LocalModIconResolver(new LocalIconTransport(r=>r.RequestUri!.AbsolutePath.Contains("version_file")?Json("{\"project_id\":\"fixture\"}"):Json("{\"icon_url\":null}")),new LocalIconTransport(_=>throw new Exception("unexpected image request"))))Check(await absent.LoadAsync(jar)==null,"mod icon: project without icon uses default");
        int requested=0;using(var blocked=new LocalModIconResolver(new LocalIconTransport(r=>r.RequestUri!.AbsolutePath.Contains("version_file")?Json("{\"project_id\":\"fixture\"}"):Json("{\"icon_url\":\"https://invalid.example/icon.png\"}")),new LocalIconTransport(_=>{requested++;return new(HttpStatusCode.OK);})))Check(await blocked.LoadAsync(jar)==null&&requested==0,"mod icon: unrelated image hosts rejected before request");
        using(var failed=new LocalModIconResolver(new LocalIconTransport(_=>throw new HttpRequestException("offline")),new LocalIconTransport(_=>throw new Exception())))Check(await failed.LoadAsync(jar)==null,"mod icon: offline safely uses default");
        var closed=new LocalModIconResolver(new LocalIconTransport(_=>throw new Exception()),new LocalIconTransport(_=>throw new Exception()));closed.Dispose();Check(await closed.LoadAsync(jar)==null,"mod icon: leaving page cancels resolver safely");
        File.WriteAllText(Path.Combine(_output,"result.txt"),$"PASS {_checks} local mod icon checks");
    }
}
