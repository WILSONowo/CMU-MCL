using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CMUmcl.Community;
using CMUmcl.Content;
internal static partial class Program
{
    private static void DirectDownloadChecks()
    {
        DirectHeaderChecks();
        byte[] bytes=Encoding.UTF8.GetBytes("fixture direct mod bytes");
        string sha1=BitConverter.ToString(SHA1.Create().ComputeHash(bytes)).Replace("-", "");
        string url=DirectUrl;
        string root = Path.Combine(Path.GetTempPath(), "CMUmcl-direct-file-" + Guid.NewGuid().ToString("N"));
        LauncherPaths.RootOverride = root; LauncherPaths.EnsureDirectories();
        var store = new InstalledStateStore();
        var targets = new ResourceTargets(store);
        // Download-only CF pack needs no existing instance; exercises actual resumable downloader/hash/commit.
        foreach (bool goodHash in new[] { true, false })
        {
            var server = new DirectCaptureHandler(r => HasDirectKey(r)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            using var install = new ResourceInstallService(targets, new MrpackImportService(store), server, () => DirectKey);
            string destination = Path.Combine(root, goodHash ? "valid.zip" : "invalid.zip");
            var result = install.InstallAsync(new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.CurseForge },
                Version = new ResourceVersion(), Kind = ResourceKind.Modpack, ModpackSavePath = destination,
                File = new ResourceFile { FileName = "pack.zip", Size = bytes.Length, Sha1 = goodHash ? sha1 : new string('0', 40), Url = url },
            }, null, CancellationToken.None).GetAwaiter().GetResult();
            if (goodHash) Check(result.Success && File.ReadAllBytes(destination).SequenceEqual(bytes), "direct-download: install verifies SHA-1 and commits file");
            else Check(!result.Success && !File.Exists(destination) && !File.Exists(destination + ".partial") && !File.Exists(destination + ".partial.json"), "direct-download: wrong SHA-1 prevents commit and cleans partial/receipt");
            Check(server.Requests.All(r => CommunityHosts.CurseForgeCdn.Contains(r.RequestUri!.Host) && HasDirectKey(r)), "direct-download: install uses only official CDN with project key");
        }
        var resumeHandler = new DirectCaptureHandler(r =>
        {
            if (r.Headers.Range == null) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenDirectBody(bytes)),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture-etag\"") } };
            int offset = (int)r.Headers.Range.Ranges.Single().From!.Value;
            var reply = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes.Skip(offset).ToArray()) };
            reply.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return reply;
        });
        using (var service = new ResourceInstallService(targets, new MrpackImportService(store), resumeHandler, () => DirectKey))
        {
            string path = Path.Combine(root, "resumed.zip");
            var result = service.InstallAsync(PackRequest(url, sha1, bytes.Length, path), null, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Success && File.ReadAllBytes(path).SequenceEqual(bytes), "direct-download: interrupted stream resumes and verifies final SHA-1");
            Check(resumeHandler.Requests.Count == 2 && resumeHandler.Requests[1].Headers.Range!.ToString() == "bytes=5-"
                && resumeHandler.Requests[1].Headers.IfRange?.EntityTag?.Tag == "\"fixture-etag\""
                && resumeHandler.Requests.All(HasDirectKey), "direct-download: Range/If-Range retry retains project key without CSU session");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var slow = new DirectCaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowDirectBody()) });
            using var service = new ResourceInstallService(targets, new MrpackImportService(store), slow, () => DirectKey);
            string path = Path.Combine(root, "cancelled.zip");
            cancellation.CancelAfter(100);
            var result = service.InstallAsync(PackRequest(url, sha1, bytes.Length, path), null, cancellation.Token).GetAwaiter().GetResult();
            Check(result.Cancelled && !File.Exists(path) && !File.Exists(path + ".partial") && !File.Exists(path + ".partial.json"),
                "direct-download: cancellation removes partial/receipt without committing final file");
        }
    }
    private static CommunityInstallRequest PackRequest(string url, string hash, long size, string path) => new CommunityInstallRequest
    {
        Project = new ResourceProject { Source = ResourceSourceId.CurseForge }, Version = new ResourceVersion(),
        Kind = ResourceKind.Modpack, ModpackSavePath = path,
        File = new ResourceFile { FileName = "pack.zip", Size = size, Sha1 = hash, Url = url },
    };
    private class SlowDirectBody : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Wait(ct);
        private static async Task<int> Wait(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override int Read(byte[] a, int b, int c) => throw new NotSupportedException(); public override void Flush() { }
        public override long Seek(long a, SeekOrigin b) => throw new NotSupportedException(); public override void SetLength(long a) => throw new NotSupportedException();
        public override void Write(byte[] a, int b, int c) => throw new NotSupportedException();
    }
    private sealed class BrokenDirectBody(byte[] bytes) : SlowDirectBody
    {
        private bool _read;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_read) throw new IOException("fixture interrupted connection");
            _read = true; Buffer.BlockCopy(bytes, 0, buffer, offset, 5); return Task.FromResult(5);
        }
    }
}
