// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CMUmcl.Community
{
    // This handler is below SafeRedirectHandler, so every accepted redirect hop gets a fresh key.
    // Icons never use this handler. CSU credentials and cookies are never sent on resource requests.
    internal sealed class CurseForgeRequestHandler : DelegatingHandler
    {
        private readonly Func<string?> _key;
        private readonly bool _api;
        internal CurseForgeRequestHandler(HttpMessageHandler inner, Func<string?> key, bool api)
        { InnerHandler = inner; _key = key; _api = api; }

        internal static bool IsOfficialFile(Uri uri) => CommunityHosts.IsSafeHttpsUri(uri)
            && CommunityHosts.CurseForgeCdn.Contains(uri.IdnHost)
            && uri.AbsolutePath.StartsWith("/files/", StringComparison.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.Remove("Authorization");
            request.Headers.Remove("X-Client-Token");
            request.Headers.Remove("Cookie");
            request.Headers.Remove("x-api-key");
            Uri? uri = request.RequestUri;
            bool officialApi = uri != null && CommunityHosts.IsSafeHttpsUri(uri)
                && string.Equals(uri.IdnHost, CommunityHosts.CurseForgeApi, StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal);
            bool protectedHost = uri != null && (string.Equals(uri.IdnHost, CommunityHosts.CurseForgeApi, StringComparison.OrdinalIgnoreCase)
                || CommunityHosts.CurseForgeCdn.Contains(uri.IdnHost));
            bool attach = uri != null && (_api ? officialApi : IsOfficialFile(uri));
            if (protectedHost && !attach)
                throw new HttpRequestException("拒绝不受支持的 CurseForge 请求地址。");
            if (attach)
            {
                string? key = _key();
                if (string.IsNullOrWhiteSpace(key) || key!.Length > 4096 || key.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new HttpRequestException("CurseForge 访问凭据未配置或无效。");
                request.Headers.TryAddWithoutValidation("x-api-key", key.Trim());
            }
            HttpResponseMessage response = await base.SendAsync(request, ct).ConfigureAwait(false);
            return response;
        }
    }
}
