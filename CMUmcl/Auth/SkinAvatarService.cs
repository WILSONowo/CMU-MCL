// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CMUmcl.Content;

namespace CMUmcl.Auth
{
    /// <summary>Public, credential-free profile/texture lookup. Stores only an 8x8 rendered head.</summary>
    internal sealed class SkinAvatarService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _cache;
        private static readonly Uri Root = new Uri(YggdrasilClient.ApiRoot + "/");
        internal SkinAvatarService(HttpMessageHandler? handler = null, string? cache = null)
        {
            _http = new HttpClient(handler ?? Network.LauncherProxy.CreateHandler(allowAutoRedirect: false, useCookies: false))
                { Timeout = TimeSpan.FromSeconds(15) };
            _cache = cache ?? Path.Combine(LauncherPaths.CacheDirectory, "avatars");
        }
        internal static string? NormalizeId(string id) => Guid.TryParse(id, out var value) && value != Guid.Empty ? value.ToString("N") : null;
        private string CachePath(string id) => Path.Combine(_cache, id + ".png");
        internal BitmapSource? LoadCached(string profileId)
        {
            var id = NormalizeId(profileId);
            if (id == null) return null;
            try
            {
                var p = CachePath(id);
                if (!File.Exists(p) || new FileInfo(p).Length > 65536) return null;
                var image = Decode(File.ReadAllBytes(p));
                return image.PixelWidth == 8 && image.PixelHeight == 8 ? image : null;
            }
            catch { return null; }
        }
        internal BitmapSource? Skin { get; private set; }
        internal bool IsSlim { get; private set; }
        internal async Task<BitmapSource?> GetAsync(string profileId, CancellationToken ct, bool force = false)
        {
            ct.ThrowIfCancellationRequested();
            var id = NormalizeId(profileId);
            if (id == null) return null;
            var cached = LoadCached(id);
            try { var path=Path.Combine(_cache,id+".skin.png"); if (new FileInfo(path).Length<=2097152) { Skin=Decode(File.ReadAllBytes(path)); RenderHead(Skin); IsSlim=File.Exists(Path.Combine(_cache,id+".slim")); } } catch { Skin=null; }
            try
            {
                if (!force && cached != null && Skin != null && DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath(id)) < TimeSpan.FromHours(6)) return cached;
                using var metadata = JsonDocument.Parse(await ReadAsync(Root, 131072, ct).ConfigureAwait(false));
                var domains = metadata.RootElement.GetProperty("skinDomains").EnumerateArray()
                    .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                using var profile = JsonDocument.Parse(await ReadAsync(new Uri(Root, "sessionserver/session/minecraft/profile/" + id), 131072, ct).ConfigureAwait(false));
                if (NormalizeId(profile.RootElement.GetProperty("id").GetString() ?? "") != id) return cached;
                var property = profile.RootElement.GetProperty("properties").EnumerateArray()
                    .First(x => x.GetProperty("name").GetString() == "textures");
                var encoded = property.GetProperty("value").GetString() ?? "";
                if (encoded.Length > 65536) return cached;
                using var textures = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                var url = textures.RootElement.GetProperty("textures").GetProperty("SKIN").GetProperty("url").GetString();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
                    uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
                    !domains.Any(d => string.Equals(d, uri.Host, StringComparison.OrdinalIgnoreCase))) return cached;
                var skinBytes = await ReadAsync(uri, 2097152, ct).ConfigureAwait(false);
                var skin = Decode(skinBytes);
                var head = RenderHead(skin);
                Skin = skin;
                var skinNode = textures.RootElement.GetProperty("textures").GetProperty("SKIN");
                IsSlim = skinNode.TryGetProperty("metadata", out var model) && model.TryGetProperty("model", out var shape) && shape.GetString()=="slim";
                ct.ThrowIfCancellationRequested();
                try
                {
                    Directory.CreateDirectory(_cache);
                    var skinPath=Path.Combine(_cache,id+".skin.png");
                    var skinTemp=skinPath+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try {File.WriteAllBytes(skinTemp,skinBytes);ct.ThrowIfCancellationRequested();if(File.Exists(skinPath))File.Replace(skinTemp,skinPath,null);else File.Move(skinTemp,skinPath);}
                    finally {if(File.Exists(skinTemp))File.Delete(skinTemp);}
                    var slimPath=Path.Combine(_cache,id+".slim");
                    if(IsSlim) File.WriteAllText(slimPath,"slim"); else if(File.Exists(slimPath)) File.Delete(slimPath);
                    var path = CachePath(id); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(head));
                        using (var output = File.Create(temp)) png.Save(output);
                        ct.ThrowIfCancellationRequested();
                        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* A read-only cache must not prevent displaying the downloaded head. */ }
                return head;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return cached; }
        }
        private async Task<byte[]> ReadAsync(Uri uri, int limit, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode(); // redirects deliberately never followed
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException();
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192]; int n;
            while ((n = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) != 0)
            { if (output.Length + n > limit) throw new InvalidDataException(); output.Write(buffer, 0, n); }
            return output.ToArray();
        }
        private static BitmapSource Decode(byte[] bytes)
        {
            // Inspect PNG header before invoking the WPF decoder, limiting allocation/decompression.
            byte[] signature = {137,80,78,71,13,10,26,10};
            if (bytes.Length < 24 || !bytes.Take(8).SequenceEqual(signature)) throw new InvalidDataException();
            long Width(int at) => ((long)bytes[at] << 24) | ((long)bytes[at+1] << 16) | ((long)bytes[at+2] << 8) | bytes[at+3];
            if (Width(16) < 1 || Width(16) > 1024 || Width(20) < 1 || Width(20) > 1024) throw new InvalidDataException();
            using var stream = new MemoryStream(bytes, false);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96,96,PixelFormats.Bgra32,null,pixels,stride);
            result.Freeze(); return result;
        }
        internal static BitmapSource RenderHead(BitmapSource skin)
        {
            int w = skin.PixelWidth, h = skin.PixelHeight;
            if (w < 64 || w > 1024 || w % 64 != 0 || (h != w && h != w / 2)) throw new InvalidDataException();
            int scale = w / 64;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(new CroppedBitmap(skin, new Int32Rect(8*scale,8*scale,8*scale,8*scale)), new Rect(0,0,8,8));
                dc.DrawImage(new CroppedBitmap(skin, new Int32Rect(40*scale,8*scale,8*scale,8*scale)), new Rect(0,0,8,8));
            }
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            var head = new RenderTargetBitmap(8,8,96,96,PixelFormats.Pbgra32); head.Render(visual); head.Freeze(); return head;
        }
        public void Dispose() => _http.Dispose();
    }
}
