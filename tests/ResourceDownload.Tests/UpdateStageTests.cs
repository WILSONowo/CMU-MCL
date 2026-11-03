using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CMUmcl.Content;
using CMUmcl.Update;

internal static class UpdateStageTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "cmumcl-update-stage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousRoot = LauncherPaths.RootOverride;
        LauncherPaths.RootOverride = root;
        string? readyStage = null;
        using var key = new ECDsaCng(256);
        UpdateSignature Sign(byte[] value) => new("ECDSA-P256-SHA256", "ieee-p1363-base64",
            Convert.ToBase64String(key.SignData(value, HashAlgorithmName.SHA256)));
        byte[] Envelope(UpdateRelease value)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, UpdateProtocol.JsonOptions);
            return JsonSerializer.SerializeToUtf8Bytes(new UpdateEnvelope(Convert.ToBase64String(payload), Sign(payload)), UpdateProtocol.JsonOptions);
        }
        try
        {
            var archive = Path.Combine(root, "fixture.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                // Synthetic payload is never run: a valid PE version is needed
                // to exercise StageAsync's final package-version guard.
                zip.CreateEntryFromFile(Assembly.GetExecutingAssembly().Location, "CMUmcl.exe");
                zip.CreateEntry("updater/CMUmcl.Updater.exe").Open().Dispose();
                using var padding = zip.CreateEntry("Assets/test.bin").Open();
                var randomPadding=Compat.RandomBytes(250000);padding.Write(randomPadding,0,randomPadding.Length);
            }
            var bytes = File.ReadAllBytes(archive);
            var hash = Compat.Hex(Compat.Sha256(bytes));
            var release = new UpdateRelease(2, "windows-x86_64", "9.0.0", "https://csumcl.mcapcs.cn/update/windows-x86_64/net48/9.0.0.zip",
                bytes.Length, hash, Sign(bytes), "isolated test");
            var envelope = Envelope(release);
            var progress = new Progress<double>(_ => { });
            using var cancellation = new CancellationTokenSource();
            using (var client = new LauncherUpdateClient(new HttpClient(new Transport(_ =>
                Response(new Interrupted(bytes, cancellation), bytes.Length))), Compat.ExportPem((ECDsaCng)key)))
            {
                var cancelled = false;
                try { await client.StageAsync(release, envelope, progress, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                var partial = Path.Combine(root, "cache", "launcher-updates", hash.ToLowerInvariant() + ".zip.partial");
                check(cancelled && File.Exists(partial) && new FileInfo(partial).Length == 60000,
                    "StageAsync cancellation retains isolated update download prefix");
            }
            long? resumedAt = null;
            using (var client = new LauncherUpdateClient(new HttpClient(new Transport(request =>
            {
                resumedAt = request.Headers.Range?.Ranges.Single().From;
                var offset = checked((int)(resumedAt ?? 0));
                var response = Response(new MemoryStream(bytes.Skip(offset).ToArray()), bytes.Length - offset);
                if (offset > 0)
                {
                    response.StatusCode = HttpStatusCode.PartialContent;
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
                }
                return response;
            })), Compat.ExportPem((ECDsaCng)key)))
            {
                readyStage = await client.StageAsync(release, envelope, progress, default);
                check(resumedAt == 60000, "new update client resumes previous StageAsync download");
                check(File.ReadAllBytes(Path.Combine(readyStage, "package.zip")).SequenceEqual(bytes), "staged update preserves exact signed ZIP bytes");
                check(File.ReadAllBytes(Path.Combine(readyStage, "latest.json")).SequenceEqual(envelope), "staged updater receives original signed envelope");
                check(!Directory.Exists(Path.Combine(readyStage, "preview")), "successful extraction preflight cleans its preview directory");
            }
            using (var client = new LauncherUpdateClient(new HttpClient(new Transport(_ => Response(new MemoryStream(bytes), bytes.Length))), Compat.ExportPem((ECDsaCng)key)))
            {
                var invalidSignature = release with { ArchiveSignature = Sign([1, 2, 3]) };
                var rejected = false;
                try { await client.StageAsync(invalidSignature, Envelope(invalidSignature), progress, default); }
                catch (CryptographicException) { rejected = true; }
                check(rejected, "resumable update still rejects wrong archive signature after hash success");
                var wrongVersion = release with { Version = "9.0.1" };
                rejected = false;
                try { await client.StageAsync(wrongVersion, Envelope(wrongVersion), progress, default); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "resumable update still rejects package/version mismatch");
            }
        }
        finally
        {
            LauncherPaths.RootOverride = previousRoot;
            if (readyStage is not null)
            {
                var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CMUmcl-updates")) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(readyStage).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe stage cleanup path");
                Directory.Delete(readyStage, recursive: true);
            }
            Directory.Delete(root, recursive: true);
        }
    }
    private static HttpResponseMessage Response(Stream stream, long length)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = length;
        return response;
    }
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(request));
    }
    private sealed class Interrupted(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (Position >= 60000) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            return base.ReadAsync(buffer,offset,Math.Min(count, 60000 - (int)Position),ct);
        }
    }
}
