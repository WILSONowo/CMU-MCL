// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CMUmcl.Community
{
    /// <summary>
    /// Session icon loader: SafeRedirectHandler + host allowlist, ≤2 MiB response, decode max edge 512,
    /// in-memory cache (never persisted), placeholder fallback on any failure.
    /// </summary>
    internal sealed class ResourceIconLoader : IDisposable
    {
        private const int MaxBytes = 8 * 1024 * 1024; // Animated official icons can exceed 2 MiB; render only the first frame.
        private const int MaxEdge = 512;

        private static HashSet<string> CreateIconHosts()
        {
            var set = new HashSet<string>(CommunityHosts.ModrinthCdn, StringComparer.OrdinalIgnoreCase);
            set.UnionWith(CommunityHosts.CurseForgeCdn);
            set.UnionWith(CommunityHosts.ResourceCdn);
            return set;
        }

        private static readonly HashSet<string> IconHosts = CreateIconHosts();

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _slots=new(4,4);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.Ordinal);

        public ResourceIconLoader()
            : this(CMUmcl.Network.LauncherProxy.CreateHandler(allowAutoRedirect: false, useCookies: false)) { }

        internal ResourceIconLoader(HttpMessageHandler inner)
        {
            _http = new HttpClient(new SafeRedirectHandler(inner, host => IconHosts.Contains(host), sameHostOnly: false))
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(CommunityHttp.UserAgent);
        }

        public async Task<BitmapSource?> LoadAsync(string? url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!CommunityHosts.TryGetHttpsHost(url, out string? host) || !IconHosts.Contains(host!)) return null;
            if (_cache.TryGetValue(url, out BitmapSource? cached)) return cached;

            bool entered=false;
            try
            {
                await _slots.WaitAsync(ct).ConfigureAwait(false);entered=true;
                if (_cache.TryGetValue(url,out cached))return cached;
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));ct=deadline.Token;
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                using var ms = new MemoryStream();
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        if (ms.Length + read > MaxBytes) return null;
                        await ms.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    }
                }
                ms.Position = 0;
                BitmapSource? bmp = Decode(ms);
                if (bmp != null) _cache[url] = bmp;
                return bmp;
            }
            catch { return null; }
            finally {if(entered)_slots.Release();}
        }

        internal static BitmapSource? Decode(Stream stream)
        {
            try
            {
                using var input=new MemoryStream();stream.CopyTo(input);byte[] bytes=input.ToArray();
                if(bytes.Length>=12 && System.Text.Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && System.Text.Encoding.ASCII.GetString(bytes,8,4)=="WEBP")
                {
                    var info=Imazen.WebP.WebPInfo.GetImageInfo(bytes);
                    if(info.Width<=0 || info.Height<=0 || info.Width>4096 || info.Height>4096 || (long)info.Width*info.Height>16777216)return null;
                    byte[] pixels;int w,h;
                    if(info.HasAnimation)
                    {
                        using var animation=new Imazen.WebP.AnimDecoder(bytes,false);
                        var frame=animation.GetNextFrame();if(frame==null)return null;pixels=frame.Pixels;w=frame.Width;h=frame.Height;
                    }
                    else pixels=Imazen.WebP.WebPDecoder.Decode(bytes,out w,out h,Imazen.WebP.WebPPixelFormat.Bgra);
                    BitmapSource image=BitmapSource.Create(w,h,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels,checked(w*4));
                    if(Math.Max(w,h)>MaxEdge)image=new TransformedBitmap(image,new System.Windows.Media.ScaleTransform(Math.Min(1,MaxEdge/(double)Math.Max(w,h)),Math.Min(1,MaxEdge/(double)Math.Max(w,h))));
                    image.Freeze();return image;
                }
                stream.Position=0;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
                if (width <= 0 || height <= 0 || width>4096 || height>4096 || (long)width*height>16777216) return null;
                stream.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                if (width >= height) bmp.DecodePixelWidth = Math.Min(width, MaxEdge);
                else bmp.DecodePixelHeight = Math.Min(height, MaxEdge);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public void Dispose() => _http.Dispose();
    }
}
