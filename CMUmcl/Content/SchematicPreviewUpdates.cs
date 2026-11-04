// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using CMUmcl.Update;

namespace CMUmcl.Content;

internal sealed record SchematicPreviewRelease(int FormatVersion, string Component, int ApiVersion,
    string Version, string Url, long Size, string Sha256, string EntrySha256,
    string MinLauncherVersion, UpdateSignature ArchiveSignature, string Notes = "");
internal sealed record SchematicPreviewOffer(SchematicPreviewRelease Release, byte[] Envelope);

internal static class SchematicPreviewProtocol
{
    internal const string FeedUrl = "https://csumcl.mcapcs.cn/components/schematic-preview/latest.json";
    internal const string UrlRoot = "/components/schematic-preview/";
    internal const long MaxArchive = 128L * 1024 * 1024;
    internal const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAETNt7NMa+hp8Xu9fv5lUZTfRzY+vd
        ctQZJ7MIiIg8I0RTAB/aYy9E4BF9wA1v8M1On8M4pby+bvvhhd/8Yu0/5g==
        -----END PUBLIC KEY-----
        """;
    internal static readonly Version CurrentLauncher = typeof(SchematicPreviewProtocol).Assembly.GetName().Version!;
    internal static void ValidateUrl(Uri uri)
    {
        string path = uri.AbsolutePath;
        if (uri.OriginalString.IndexOf('%') >= 0 || uri.Scheme != "https" || !uri.IsDefaultPort || uri.Host != "csumcl.mcapcs.cn" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !path.StartsWith(UrlRoot, StringComparison.Ordinal) || !path.EndsWith(".zip", StringComparison.Ordinal) ||
            path.Substring(UrlRoot.Length).Any(c => !(Compat.IsAsciiDigit(c) || c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '.' or '_' or '-')) ||
            path.Substring(UrlRoot.Length).Length < 5)
            throw new InvalidDataException("扩展下载地址不属于指定的 HTTPS 组件通道。");
    }
    internal static bool IsHash(string? value) => value?.Length == 64 && value.All(Compat.IsAsciiHexDigit);
    internal static void Verify(byte[] bytes, UpdateSignature signature, string publicKey, bool hash = false)
    {
        if (signature == null || signature.Algorithm != "ECDSA-P256-SHA256" || signature.Format != "ieee-p1363-base64")
            throw new CryptographicException("扩展签名格式无效。");
        byte[] sig = Convert.FromBase64String(signature.Value);
        if (sig.Length != 64) throw new CryptographicException("扩展签名长度无效。");
        using var key = Compat.CreateEcdsa(publicKey);
        if (!(hash ? key.VerifyHash(bytes, sig) : key.VerifyData(bytes, sig, HashAlgorithmName.SHA256)))
            throw new CryptographicException("扩展签名校验失败。");
    }
    internal static SchematicPreviewRelease Read(byte[] bytes, string publicKey = PublicKey)
    {
        if (bytes.Length > 128 * 1024) throw new InvalidDataException("扩展清单过大。");
        using var json = JsonDocument.Parse(bytes);
        EnsureUnique(json.RootElement);
        var envelope = JsonSerializer.Deserialize<UpdateEnvelope>(bytes, UpdateProtocol.JsonOptions) ?? throw new InvalidDataException("扩展清单为空。");
        byte[] payload = Convert.FromBase64String(envelope.Payload);
        Verify(payload, envelope.Signature, publicKey);
        using var body = JsonDocument.Parse(payload); EnsureUnique(body.RootElement);
        var release = JsonSerializer.Deserialize<SchematicPreviewRelease>(payload, UpdateProtocol.JsonOptions) ?? throw new InvalidDataException("扩展信息为空。");
        if (release.FormatVersion != 1 || release.Component != SchematicPreviewSession.ComponentId || release.ApiVersion != 2)
            throw new InvalidDataException("扩展类型或接口版本不匹配。");
        _ = UpdateProtocol.ParseVersion(release.Version);
        if (UpdateProtocol.ParseVersion(release.MinLauncherVersion) > CurrentLauncher)
            throw new InvalidDataException("此扩展需要更新的客户端，请先更新客户端。");
        if (release.Size <= 0 || release.Size > MaxArchive || !IsHash(release.Sha256) || !IsHash(release.EntrySha256) || release.Notes?.Length > 8192)
            throw new InvalidDataException("扩展大小、哈希或说明无效。");
        ValidateUrl(new Uri(release.Url, UriKind.Absolute));
        if (release.ArchiveSignature == null || release.ArchiveSignature.Algorithm != "ECDSA-P256-SHA256" ||
            release.ArchiveSignature.Format != "ieee-p1363-base64" || Convert.FromBase64String(release.ArchiveSignature.Value).Length != 64)
            throw new CryptographicException("扩展包签名格式无效。");
        return release;
    }
    private static void EnsureUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("清单包含重复字段。"); EnsureUnique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) EnsureUnique(item);
    }
    internal static void VerifyArchive(string file, SchematicPreviewRelease release, string publicKey)
    {
        using var input = File.OpenRead(file);
        if (input.Length != release.Size) throw new InvalidDataException("扩展包大小不匹配。");
        byte[] hash = Compat.Sha256(input);
        if (!Compat.Hex(hash).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("扩展包哈希校验失败。");
        Verify(hash, release.ArchiveSignature, publicKey, true);
    }
    internal static void Extract(string archive, string destination, CancellationToken cancellation)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count == 0 || zip.Entries.Count > 2048) throw new InvalidDataException("扩展包文件数量超限。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/'); bool directory = name.EndsWith("/", StringComparison.Ordinal); name = name.TrimEnd('/');
            var parts = name.Split('/');
            int kind = (entry.ExternalAttributes >> 16) & 0xF000;
            if (name.Length > 240 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(".") || p.EndsWith(" ") ||
                p.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*', '\0' }) >= 0 || p.Any(char.IsControl) || DeviceName(p)) ||
                !names.Add(name) || (kind != 0 && kind != 0x8000 && kind != 0x4000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                !(name.StartsWith("Licenses/", StringComparison.Ordinal) || (directory && name == "Licenses") ||
                  (!directory && new[] { "component.json", "SchematicPreviewWorker.exe", "LICENSE", "NOTICE", "SOURCE.txt", "Source.zip" }.Contains(name, StringComparer.Ordinal))))
                throw new InvalidDataException("扩展包包含不安全或未经允许的路径：" + name);
            total = checked(total + entry.Length);
            if (entry.Length > MaxArchive || total > 256L * 1024 * 1024) throw new InvalidDataException("扩展解压大小超限。");
        }
        if (!new[] { "component.json", "SchematicPreviewWorker.exe", "LICENSE", "NOTICE", "SOURCE.txt" }.All(names.Contains))
            throw new InvalidDataException("扩展包缺少必要文件。");
        UpdateProtocol.EnsureFreeSpace(destination, total);
        Directory.CreateDirectory(destination);
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/');
            string path = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(destination).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("解压目录越界。");
            SchematicPreviewSession.CheckRegularPath(path);
            if (relative.EndsWith("/")) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
            var buffer = new byte[65536]; long copied = 0; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { cancellation.ThrowIfCancellationRequested(); copied += count; if (copied > entry.Length) throw new InvalidDataException("解压内容超出声明大小。"); output.Write(buffer, 0, count); }
            if (copied != entry.Length) throw new InvalidDataException("扩展文件不完整。");
        }
    }
    private static bool DeviceName(string value)
    {
        string stem = value.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "0123456789¹²³".Contains(stem[3]));
    }
}

internal sealed class SchematicPreviewUpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _key;
    internal SchematicPreviewUpdateClient() : this(new HttpClient(CMUmcl.Network.LauncherProxy.CreateHandler(allowAutoRedirect: false)) { Timeout = Timeout.InfiniteTimeSpan }, SchematicPreviewProtocol.PublicKey) { }
    internal SchematicPreviewUpdateClient(HttpClient http, string key) { _http = http; _key = key; }
    internal async Task<SchematicPreviewOffer> CheckAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        byte[] envelope = await GetBytes(new Uri(SchematicPreviewProtocol.FeedUrl), 128 * 1024, timeout.Token).ConfigureAwait(false);
        return new(SchematicPreviewProtocol.Read(envelope, _key), envelope);
    }
    private async Task<HttpResponseMessage> Get(Uri uri, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) { response.Dispose(); throw new IOException("预览扩展尚未发布到在线通道，请稍后重试。"); }
        if (!response.IsSuccessStatusCode) { int code = (int)response.StatusCode; response.Dispose(); throw new IOException("扩展服务器返回 " + code + "，未安装任何内容。"); }
        if (response.RequestMessage?.RequestUri != null && response.RequestMessage.RequestUri != uri) { response.Dispose(); throw new IOException("扩展下载发生了未经允许的重定向。"); }
        return response;
    }
    private async Task<byte[]> GetBytes(Uri uri, long maximum, CancellationToken cancellation)
    {
        using var response = await Get(uri, cancellation).ConfigureAwait(false);
        using var memory = new MemoryStream(); await Copy(response, memory, maximum, cancellation).ConfigureAwait(false); return memory.ToArray();
    }
    private static async Task<long> Copy(HttpResponseMessage response, Stream output, long maximum, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("下载内容超过大小限制。");
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var progressClock = System.Diagnostics.Stopwatch.StartNew();
        byte[] buffer = new byte[65536]; long copied = 0; int count;
        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
        {
            copied += count; if (copied > maximum) throw new InvalidDataException("下载内容超过大小限制。");
            await output.WriteAsync(buffer, 0, count, cancellation).ConfigureAwait(false);
            if (copied == maximum || progressClock.ElapsedMilliseconds >= 100) { progress?.Report($"下载预览扩展…{copied / 1048576.0:F1} / {maximum / 1048576.0:F1} MiB"); progressClock.Restart(); }
        }
        return copied;
    }
    internal async Task InstallAsync(SchematicPreviewOffer offer, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        byte[] envelope = (byte[])offer.Envelope.Clone();
        var release = SchematicPreviewProtocol.Read(envelope, _key);
        if (release != offer.Release) throw new InvalidDataException("扩展信息在确认后发生变化。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(5)); var ct = timeout.Token;
        using var installLock = SchematicPreviewSession.AcquireInstallLock();
        string? active = SchematicPreviewSession.InstalledVersion();
        if (active != null && UpdateProtocol.ParseVersion(release.Version) < UpdateProtocol.ParseVersion(active)) throw new InvalidDataException("扩展通道返回了旧版本，已拒绝降级。");
        string stage = Path.Combine(SchematicPreviewSession.ComponentRoot, "staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            string archive = Path.Combine(stage, "package.zip");
            using (var response = await Get(new Uri(release.Url), ct).ConfigureAwait(false))
            using (var output = File.Create(archive))
                if (await Copy(response, output, release.Size, ct, progress).ConfigureAwait(false) != release.Size) throw new InvalidDataException("扩展下载不完整。");
            progress?.Report("校验扩展签名与文件…");
            await Task.Run(() => SchematicPreviewProtocol.VerifyArchive(archive, release, _key), ct).ConfigureAwait(false);
            string unpacked = Path.Combine(stage, "unpacked");
            await Task.Run(() => SchematicPreviewProtocol.Extract(archive, unpacked, ct), ct).ConfigureAwait(false);
            if (SchematicPreviewSession.ValidateFolder(unpacked) != release.Version || SchematicPreviewSession.Hash(Path.Combine(unpacked, "SchematicPreviewWorker.exe")) != release.EntrySha256.ToLowerInvariant())
                throw new CryptographicException("扩展版本或入口哈希与签名清单不符。");
            File.WriteAllBytes(Path.Combine(unpacked, "signed-feed.json"), envelope);
            ct.ThrowIfCancellationRequested();
            progress?.Report("激活预览扩展…");
            await Task.Run(() => SchematicPreviewSession.CommitVersion(unpacked, release.Version, "online-signed"), ct).ConfigureAwait(false);
        }
        finally { await Task.Run(() => SchematicPreviewSession.RemoveOwnedDirectory(stage, SchematicPreviewSession.ComponentRoot, "staging-")).ConfigureAwait(false); }
    }
    public void Dispose() => _http.Dispose();
}
