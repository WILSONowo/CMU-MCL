// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CMUmcl.Community
{
    /// <summary>
    /// Manual per-hop redirect handler. Never relies on HttpClientHandler auto-redirect (which can
    /// forward x-api-key to another host and allow HTTPS→HTTP downgrade). Validates every hop:
    /// only 301/302/303/307/308, ≤5 hops, loop rejection, per-hop absolute-HTTPS + host allowlist,
    /// 307/308 preserve method/body, 303 → GET, cross-host strips x-api-key/Authorization (Cookie is
    /// disabled at the client level). CurseForge API clients pass sameHostOnly=true.
    /// </summary>
    internal sealed class SafeRedirectHandler : DelegatingHandler
    {
        private const int MaxRedirects = 5;
        private static readonly int[] RedirectCodes = { 301, 302, 303, 307, 308 };

        private readonly Func<string, bool> _hostAllowed;
        private readonly bool _sameHostOnly;

        public SafeRedirectHandler(HttpMessageHandler inner, Func<string, bool> hostAllowed, bool sameHostOnly)
        {
            InnerHandler = inner ?? throw new ArgumentNullException(nameof(inner));
            _hostAllowed = hostAllowed ?? throw new ArgumentNullException(nameof(hostAllowed));
            _sameHostOnly = sameHostOnly;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri == null) throw new HttpRequestException("请求缺少地址。");
            string originalHost = request.RequestUri.Host;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HttpRequestMessage current = request;

            for (int hop = 0; ; hop++)
            {
                if (hop > MaxRedirects) throw new HttpRequestException("重定向次数过多。");
                Uri uri = current.RequestUri!;
                if (!CommunityHosts.TryGetHttpsHost(uri.AbsoluteUri, out string? host))
                    throw new HttpRequestException("仅允许 HTTPS 地址。");
                if (!_hostAllowed(host!))
                    throw new HttpRequestException($"主机不在允许列表内：{host}");
                if (_sameHostOnly && !string.Equals(host, originalHost, StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException("拒绝跨主机重定向。");
                if (!visited.Add(uri.AbsoluteUri))
                    throw new HttpRequestException("检测到重定向循环。");

                HttpResponseMessage response = await base.SendAsync(current, cancellationToken).ConfigureAwait(false);
                int code = (int)response.StatusCode;
                if (Array.IndexOf(RedirectCodes, code) < 0) return response;

                Uri? location = response.Headers.Location;
                if (location == null) { response.Dispose(); throw new HttpRequestException("重定向缺少 Location。"); }
                Uri nextUri = location.IsAbsoluteUri ? location : new Uri(uri, location);

                // Drain + dispose the redirect response before the next hop (connection reuse, no leak).
                await DrainAsync(response, cancellationToken).ConfigureAwait(false);

                bool crossHost = !string.Equals(nextUri.Host, uri.Host, StringComparison.OrdinalIgnoreCase);
                HttpRequestMessage next = await CloneAsync(current, nextUri).ConfigureAwait(false);
                if (crossHost)
                {
                    next.Headers.Remove("x-api-key");
                    next.Headers.Remove("Authorization");
                    next.Headers.Remove("X-Client-Token");
                    next.Headers.Remove("Cookie");
                }
                if (code == 303 && next.Method != HttpMethod.Get)
                {
                    next.Method = HttpMethod.Get;
                    next.Content = null;
                }
                // 307/308 preserve method+body; Range/If-Range are ordinary headers preserved by CloneAsync.
                current = next;
            }
        }

        private static async Task DrainAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                if (response.Content != null)
                {
                    using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    var buffer = new byte[8192];
                    while (await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false) > 0) { }
                }
            }
            catch { /* best effort */ }
            finally { response.Dispose(); }
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original, Uri newUri)
        {
            var clone = new HttpRequestMessage(original.Method, newUri) { Version = original.Version };
            foreach (var header in original.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var property in original.Properties) clone.Properties.Add(property.Key, property.Value);
            if (original.Content != null)
            {
                byte[] bytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var content = new ByteArrayContent(bytes);
                foreach (var header in original.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                clone.Content = content;
            }
            return clone;
        }

    }
}
