using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMUmcl.Content;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("ok: " + name); }
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var manifest = Encoding.UTF8.GetBytes("""{"formatVersion":1,"instances":[]}""");
var signature = JsonSerializer.SerializeToUtf8Bytes(new
{
    algorithm = "ECDSA-P256-SHA256",
    signature = Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256))
});
using (var handler = new Handler((request, _) => Reply(request.RequestUri!.AbsolutePath.EndsWith(".sig") ? signature : manifest)))
using (var client = new ResourceManifestClient(handler, new Uri("https://cdn.example.com/"), key.ExportSubjectPublicKeyInfoPem()))
{
    Check((await client.GetManifestAsync()).FormatVersion == 1, "valid signed pair accepted");
    Check(handler.Calls == 2, "valid pair needs exactly two requests");
}
foreach (var (url, expected, description) in new[]
{
    ($"https://{ResourceManifestClient.AllowedPackHost}/packs/example.mrpack", true, "public product host"),
    ($"https://{ResourceManifestClient.AllowedPackHost.ToUpperInvariant()}/packs/example.mrpack", true, "case-insensitive product host"),
    ($"http://{ResourceManifestClient.AllowedPackHost}/packs/example.mrpack", false, "unencrypted transport"),
    ($"https://{ResourceManifestClient.AllowedPackHost}.example.invalid/packs/example.mrpack", false, "host suffix spoofing"),
    ("https://198.51.100.10/packs/example.mrpack", false, "unlisted IP endpoint"),
    ("https://downloads.example.invalid/packs/example.mrpack", false, "unlisted domain")
})
{
    byte[] packManifest = JsonSerializer.SerializeToUtf8Bytes(new
    {
        formatVersion = 1,
        instances = new[] { new { id = "allowlist-fixture", pack = new { url } } }
    });
    byte[] packSignature = JsonSerializer.SerializeToUtf8Bytes(new
    {
        algorithm = "ECDSA-P256-SHA256",
        signature = Convert.ToBase64String(key.SignData(packManifest, HashAlgorithmName.SHA256))
    });
    using var handler = new Handler((request, _) => Reply(request.RequestUri!.AbsolutePath.EndsWith(".sig") ? packSignature : packManifest));
    using var client = new ResourceManifestClient(handler, new Uri("https://cdn.example.com/"), key.ExportSubjectPublicKeyInfoPem());
    bool accepted = true;
    try { await client.GetManifestAsync(); } catch (ResourceException) { accepted = false; }
    Check(accepted == expected, "signed resource URL policy: " + description);
}
using (var handler = new Handler((request, _) =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith(".sig")) return Reply(signature);
    return Reply(request.RequestUri.Query.Length == 0 ? Encoding.UTF8.GetBytes("{\"old\":true}") : manifest);
}))
using (var client = new ResourceManifestClient(handler, new Uri("https://cdn.example.com/"), key.ExportSubjectPublicKeyInfoPem()))
{
    Check((await client.GetManifestAsync()).FormatVersion == 1, "publication/cache mismatch recovers with fresh verified pair");
    Check(handler.Calls == 4 && handler.RefreshCalls == 2, "retry revalidates both URLs and bypasses stale cache key");
}
using (var handler = new Handler((request, _) => Reply(request.RequestUri!.AbsolutePath.EndsWith(".sig") ? signature : [1, 2, 3])))
using (var client = new ResourceManifestClient(handler, new Uri("https://cdn.example.com/"), key.ExportSubjectPublicKeyInfoPem()))
{
    var rejected = false;
    try { await client.GetManifestAsync(); } catch (ResourceException) { rejected = true; }
    Check(rejected && handler.Calls == 6, "persistent invalid signature fails closed after bounded retries");
}
using (var handler = new Handler((_, _) => Reply(new byte[4 * 1024 * 1024 + 1])))
using (var client = new ResourceManifestClient(handler, new Uri("https://cdn.example.com/"), key.ExportSubjectPublicKeyInfoPem()))
{
    var rejected = false;
    try { await client.GetManifestAsync(); } catch (ResourceException) { rejected = true; }
    Check(rejected, "oversized manifest cannot exhaust memory");
}
using (var handler = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
using (var client = new ResourceManifestClient(handler, new Uri("https://cdn.example.com/"), key.ExportSubjectPublicKeyInfoPem()))
{
    var rejected = false;
    try { await client.GetManifestAsync(); } catch (ResourceException) { rejected = true; }
    Check(rejected, "HTTP errors remain user-facing ResourceException");
}
await ResumeTests.Run(Check);
await UpdateStageTests.Run(Check);
if (args.Length == 2 && args[0] == "--backend-assembly")
    await BackendInstallTests.Run(args[1], Check);
else if (args.Length != 0) throw new ArgumentException("Expected --backend-assembly <PclCmu.Admin.dll>");
Console.WriteLine($"{passed} resource download checks passed.");

static HttpResponseMessage Reply(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handle) : HttpMessageHandler
{
    public int Calls;
    public int RefreshCalls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (request.Headers.CacheControl?.NoCache == true && request.RequestUri!.Query.Length > 0) Interlocked.Increment(ref RefreshCalls);
        return Task.FromResult(handle(request, ct));
    }
}
